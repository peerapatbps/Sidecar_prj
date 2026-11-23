using System.Globalization;
using System.Text.Json;

namespace Uroboros.MiniUploader;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // WinExe อาจไม่มี console ผูกอยู่ (เปิดจาก launcher) — ตั้ง encoding แล้วอาจ throw ได้
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* no console attached */ }

        // Offline self-test: merge sidecar.db into a throwaway chem db and print the result.
        //   Uroboros.MiniUploader.exe --sidecar-test [sidecar.db] [outDb]
        // With no path, uses SidecarDb from mini-uploader.json (proves config wiring).
        if (args.Length >= 1 && string.Equals(args[0], "--sidecar-test", StringComparison.OrdinalIgnoreCase))
        {
            var testLog = new FileLogger(Path.Combine(AppContext.BaseDirectory, "logs"));
            var sidecarDb = args.Length >= 2 ? args[1] : null;
            if (string.IsNullOrWhiteSpace(sidecarDb))
            {
                var cfg = MiniUploaderConfig.Load(Path.Combine(AppContext.BaseDirectory, "mini-uploader.json"));
                sidecarDb = SidecarMerge.ResolvePath(cfg.SidecarDb, AppContext.BaseDirectory);
                Console.WriteLine($"Using SidecarDb from config: '{cfg.SidecarDb}' -> '{sidecarDb}'");
                if (string.IsNullOrWhiteSpace(sidecarDb))
                {
                    Console.Error.WriteLine("SidecarDb is empty in mini-uploader.json and no path was given.");
                    return 1;
                }
            }
            var outDb = args.Length >= 3
                ? args[2]
                : Path.Combine(Path.GetTempPath(), "chem.sidecar-test.db");
            return SidecarMerge.SelfTest(sidecarDb, outDb, testLog);
        }

        using var singleInstance = new SingleInstanceGuard("Uroboros.MiniUploader");
        if (!singleInstance.HasHandle)
        {
            Console.WriteLine("Another instance is already running.");
            return 2;
        }

        var startupPath = AppContext.BaseDirectory;
        var logger = new FileLogger(Path.Combine(startupPath, "logs"));

        try
        {
            var configPath = Path.Combine(startupPath, "mini-uploader.json");
            var config = MiniUploaderConfig.Load(configPath);
            var scheduler = new MiniUploadScheduler(config, startupPath, logger);

            logger.Info("Mini uploader started.");
            logger.Info($"Config file: {configPath}");
            logger.Info($"INI path: {Path.Combine(startupPath, config.ConfigFolderName, config.ConfigFileName)}");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (!cts.IsCancellationRequested)
                {
                    logger.Warn("Ctrl+C received. Stopping...");
                    cts.Cancel();
                }
            };

            await scheduler.RunAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // clean shutdown — cancellation escaped RunAsync
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Fatal error");
            return 1;
        }

        logger.Info("Mini uploader stopped.");
        return 0;
    }
}

internal sealed class MiniUploadScheduler
{
    private readonly MiniUploaderConfig _config;
    private readonly string _startupPath;
    private readonly ILogger _log;
    private readonly UploaderStateStore _stateStore;
    private readonly MdbUploadService _service;
    private readonly IotRoomClient _iotClient;
    private int _running;

    public MiniUploadScheduler(MiniUploaderConfig config, string startupPath, ILogger log)
    {
        _config = config;
        _startupPath = startupPath;
        _log = log;
        _stateStore = new UploaderStateStore(Path.Combine(_startupPath, config.StateFileName));
        _service = new MdbUploadService(config, startupPath, log);
        _iotClient = new IotRoomClient(config, log);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var heartbeat = _iotClient.IsEnabled
            ? Task.Run(() => _iotClient.RunHeartbeatAsync(ct), ct)
            : Task.CompletedTask;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var gate = ScheduleGate.Check(_config, now);

                if (!gate.ShouldRun)
                {
                    _log.Info($"Waiting | now={now:yyyy-MM-dd HH:mm:ss} | next-window={gate.Description}");
                }
                else if (Interlocked.Exchange(ref _running, 1) == 1)
                {
                    _log.Warn("Previous cycle is still running; skip this tick.");
                }
                else
                {
                    try
                    {
                        var state = _stateStore.Load();
                        if (!string.IsNullOrWhiteSpace(gate.SlotKey) &&
                            string.Equals(state.LastProcessedScheduleSlot, gate.SlotKey, StringComparison.Ordinal))
                        {
                            _log.Info($"Skip duplicate schedule slot: {gate.SlotKey}");
                        }
                        else
                        {
                            var result = await _service.RunOnceAsync(ct, state.LastUploadedStamp).ConfigureAwait(false);
                            _log.Info(result.Message);

                            if (!string.IsNullOrWhiteSpace(gate.SlotKey))
                            {
                                state.LastProcessedScheduleSlot = gate.SlotKey;
                            }

                            state.LastRunAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                            state.LastResult = result.Message;
                            state.LastSourceStamp = result.SourceStamp ?? state.LastSourceStamp;
                            state.LastUploadedStamp = result.UploadedStamp ?? state.LastUploadedStamp;
                            _stateStore.Save(state);

                            var payload = new IotStatusPayload
                            {
                                LastRunAt = state.LastRunAtLocal,
                            };
                            _ = _iotClient.SendDataAsync(payload, ct);
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Scheduler tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.PollIntervalSeconds)), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        try { await heartbeat.ConfigureAwait(false); } catch { }
        await _iotClient.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class MdbUploadService
{
    private readonly MiniUploaderConfig _config;
    private readonly string _startupPath;
    private readonly ILogger _log;

    public MdbUploadService(MiniUploaderConfig config, string startupPath, ILogger log)
    {
        _config = config;
        _startupPath = startupPath;
        _log = log;
    }

    public async Task<UploadCycleResult> RunOnceAsync(CancellationToken ct, string? lastUploadedStamp = null)
    {
        var configRel = Path.Combine(_config.ConfigFolderName, _config.ConfigFileName);
        string? syncMsg;
        var syncStatus = MdbReaderSafe.SyncMdbToMediaAtStartup(_startupPath, configRel, out syncMsg);

        var mediaMdbPath = Path.Combine(_startupPath, MdbReaderSafe.MediaFolderName, MdbReaderSafe.MediaMdbName);
        // รวม stamp ของ sidecar records ด้วย: บันทึกใหม่ใน sidecar (mdb ไม่เปลี่ยน) ต้อง upload ใหม่
        var sidecarStamp = SidecarMerge.Stamp(SidecarMerge.ResolvePath(_config.SidecarDb, _startupPath));
        // hash เนื้อไฟล์ ไม่ใช้ mtime (mtime ไม่ขยับขณะโปรแกรมชั่งเปิด data.mdb ค้าง → เคยทำให้ไม่ upload ของใหม่)
        var mdbHash = MdbReaderSafe.MediaMdbHash(_startupPath);
        var sourceStamp = File.Exists(mediaMdbPath) && mdbHash.Length > 0
            ? mdbHash + sidecarStamp
            : null;

        if (syncStatus == MdbReaderSafe.MdbSyncStatus.UpToDate)
        {
            // Only skip upload if we already uploaded this exact stamp.
            // If lastUploadedStamp differs (e.g. previous upload failed), fall through to re-upload.
            if (string.Equals(sourceStamp, lastUploadedStamp, StringComparison.Ordinal))
                return new UploadCycleResult(true, syncMsg ?? "MDB already up-to-date.", sourceStamp, null);

            _log.Info($"MDB up-to-date but upload stamp mismatch (source={sourceStamp}, uploaded={lastUploadedStamp}); re-uploading.");
        }
        else if (syncStatus != MdbReaderSafe.MdbSyncStatus.Synced)
        {
            return new UploadCycleResult(false, "Sync failed: " + syncMsg, sourceStamp, null);
        }

        if (syncStatus == MdbReaderSafe.MdbSyncStatus.Synced)
            _log.Info($"Sync complete: {syncMsg}");

        var converted = MdbReaderSafe.ConvertMediaMdbToChemDb(_startupPath, _log, ct, _config.SidecarDb);
        var activeChemDbPath = MdbReaderSafe.ResolveActiveChemDbPath(_startupPath);

        if (!File.Exists(activeChemDbPath))
        {
            return new UploadCycleResult(false, $"Active chem DB missing after convert ({activeChemDbPath})", sourceStamp, null);
        }

        var service = GoogleDriveHelper.GetDriveServiceOrThrow(_startupPath);
        var upload = await DriveSyncCli
            .UploadFileToSpecificFolderAsNameAsync(
                service,
                activeChemDbPath,
                _config.DriveFolderId,
                _config.DriveRemoteChemFileName,
                ct)
            .ConfigureAwait(false);

        var uploadedStamp = sourceStamp;
        var fi = new FileInfo(activeChemDbPath);
        var sizeMb = fi.Length / 1024d / 1024d;
        var prefix = converted ? "Upload done" : "Upload refreshed";
        var mode = upload.UpdatedExisting ? "updated" : "created";

        return new UploadCycleResult(
            true,
            $"{prefix}: {upload.RemoteFileName} ({sizeMb:F2} MB) | {mode} | source-stamp={sourceStamp}",
            sourceStamp,
            uploadedStamp);
    }
}

internal sealed record UploadCycleResult(bool Ok, string Message, string? SourceStamp, string? UploadedStamp);

internal sealed class MiniUploaderConfig
{
    public string DriveFolderId { get; set; } = "1hLIPn9qgjqm4WliNJGsEwmjq78oaXFgm";
    public string DriveRemoteChemFileName { get; set; } = "chem.db";
    public string ConfigFolderName { get; set; } = "config_";
    public string ConfigFileName { get; set; } = "config.ini";
    public int PollIntervalSeconds { get; set; } = 30;
    public int ScheduleWindowMinutes { get; set; } = 2;
    public List<string> DailyTimes { get; set; } = [];
    public string StateFileName { get; set; } = "uploader-state.json";

    // TruckSidecar integration — path to the sidecar's SQLite store
    // (e.g. ..\TruckSidecar\data\sidecar.db; relative = from this exe's folder). Empty = disabled.
    // Its SIDECAR_UNLOAD / SIDECAR_FILL tables are merged into chem.db each time the MDB
    // or the sidecar store changes, then uploaded to Drive with the rest of the snapshot.
    public string SidecarDb { get; set; } = "";

    // IoT Room integration — set IotEnabled: true to activate
    public bool IotEnabled { get; set; } = false;
    public string IotServerUrl { get; set; } = "http://172.16.198.150:443";
    public string IotDeviceName { get; set; } = "truck weighing scale";
    public string IotDeviceType { get; set; } = "Computer";
    public int IotPollIntervalSeconds { get; set; } = 15;

    public static MiniUploaderConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var cfg = new MiniUploaderConfig();
            var json = JsonSerializer.Serialize(cfg, JsonOptions.Indented);
            File.WriteAllText(path, json);
            return cfg;
        }

        var text = File.ReadAllText(path);
        var model = JsonSerializer.Deserialize<MiniUploaderConfig>(text, JsonOptions.CaseInsensitive);
        return model ?? new MiniUploaderConfig();
    }
}

internal sealed class UploaderState
{
    public string? LastProcessedScheduleSlot { get; set; }
    public string? LastRunAtLocal { get; set; }
    public string? LastResult { get; set; }
    public string? LastSourceStamp { get; set; }
    public string? LastUploadedStamp { get; set; }
}

internal sealed class UploaderStateStore
{
    private readonly string _path;

    public UploaderStateStore(string path)
    {
        _path = path;
    }

    public UploaderState Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new UploaderState();

            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UploaderState>(text, JsonOptions.CaseInsensitive) ?? new UploaderState();
        }
        catch
        {
            return new UploaderState();
        }
    }

    public void Save(UploaderState state)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions.Indented));
        if (File.Exists(_path))
            File.Replace(temp, _path, null, ignoreMetadataErrors: true);
        else
            File.Move(temp, _path);
    }
}

internal static class ScheduleGate
{
    public static ScheduleDecision Check(MiniUploaderConfig config, DateTime now)
    {
        if (config.DailyTimes.Count == 0)
            return new ScheduleDecision(true, null, "always");

        foreach (var item in config.DailyTimes)
        {
            if (!TimeSpan.TryParseExact(item, "hh\\:mm", CultureInfo.InvariantCulture, out var hhmm))
                continue;

            var scheduled = now.Date.Add(hhmm);
            var until = scheduled.AddMinutes(Math.Max(1, config.ScheduleWindowMinutes));

            if (now >= scheduled && now < until)
            {
                var slotKey = scheduled.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
                return new ScheduleDecision(true, slotKey, slotKey);
            }
        }

        var upcoming = config.DailyTimes
            .Select(x => TimeSpan.TryParseExact(x, "hh\\:mm", CultureInfo.InvariantCulture, out var t) ? t : (TimeSpan?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x)
            .FirstOrDefault(x => now.TimeOfDay < x);

        var desc = upcoming == default
            ? "tomorrow"
            : now.Date.Add(upcoming).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return new ScheduleDecision(false, null, desc);
    }
}

internal sealed record ScheduleDecision(bool ShouldRun, string? SlotKey, string Description);

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
