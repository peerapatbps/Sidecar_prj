using System.Diagnostics;
using System.Text.Json;

namespace TruckWatchdog;

// ตัวเฝ้า (watchdog): ขณะที่โปรแกรมชั่งเดิม (truck93a) ทำงานอยู่ ให้คอย "เปิด sidecar กลับ"
// ถ้ามันถูกปิดโดยไม่ตั้งใจหรือ crash — เพื่อบังคับให้ sidecar เปิดคู่กับตาชั่งเสมอ
//
// เคารพทางออกของผู้ดูแลระบบ (Ctrl+Shift+F12): ก่อนปิดตัวเอง sidecar จะเขียนธง
// data/sidecar.stopped ไว้ ถ้าเจอธงนี้ watchdog จะ "ไม่" เปิด sidecar ใหม่
// เมื่อโปรแกรมชั่งปิด watchdog จะลบธงแล้วจบตัวเอง (รอบหน้า launcher เริ่มใหม่หมด)
internal static class Program
{
    sealed class WatchdogConfig
    {
        public string TruckProcess { get; set; } = "truck93a";
        public string SidecarProcess { get; set; } = "TruckSidecar";
        public string SidecarExe { get; set; } = "";
        public string StopFlagPath { get; set; } = "";
        public int PollSeconds { get; set; } = 5;
        public int StartupGraceSeconds { get; set; } = 60;
    }

    sealed class SidecarConfigPeek
    {
        public string DataDir { get; set; } = "data";
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [STAThread]
    static void Main()
    {
        var baseDir = AppContext.BaseDirectory;

        WatchdogConfig cfg;
        try
        {
            var json = File.ReadAllText(Path.Combine(baseDir, "watchdog.json"));
            cfg = JsonSerializer.Deserialize<WatchdogConfig>(json, JsonOpts) ?? new WatchdogConfig();
        }
        catch (Exception ex)
        {
            Warn("อ่าน watchdog.json ไม่ได้: " + ex.Message);
            return;
        }

        if (!TryResolve(cfg.SidecarExe, baseDir, out var sidecarExe))
        {
            Warn($"ไม่พบ TruckSidecar.exe:\n{cfg.SidecarExe}");
            return;
        }

        // มีตัวเดียวพอ — กัน watchdog ซ้อน
        using var mutex = new Mutex(true, "TruckWatchdog.SingleInstance", out bool first);
        if (!first) return;

        var stopFlag = ResolveStopFlag(cfg.StopFlagPath, sidecarExe);
        int poll = Math.Max(1, cfg.PollSeconds) * 1000;
        var deadline = DateTime.Now.AddSeconds(Math.Max(0, cfg.StartupGraceSeconds));
        bool truckSeen = false;

        while (true)
        {
            bool truckRunning = IsRunning(cfg.TruckProcess);

            if (truckRunning)
            {
                truckSeen = true;
                bool sidecarRunning = IsRunning(cfg.SidecarProcess);
                bool adminStopped = File.Exists(stopFlag);
                if (!sidecarRunning && !adminStopped)
                    StartSidecar(sidecarExe);
            }
            else if (truckSeen || DateTime.Now >= deadline)
            {
                // ตาชั่งปิดแล้ว (หรือรอเปิดนานเกินไป) — เลิกเฝ้า ล้างธงให้รอบหน้าเริ่มสะอาด
                ClearStopFlag(stopFlag);
                return;
            }

            Thread.Sleep(poll);
        }
    }

    static string ResolveStopFlag(string overridePath, string sidecarExe)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        // เลียนแบบวิธี sidecar คำนวณ dataDir: อ่าน config.json ข้าง sidecar.exe
        var sidecarDir = Path.GetDirectoryName(sidecarExe)!;
        var dataDir = "data";
        try
        {
            var peek = JsonSerializer.Deserialize<SidecarConfigPeek>(
                File.ReadAllText(Path.Combine(sidecarDir, "config.json")), JsonOpts);
            if (peek != null && !string.IsNullOrWhiteSpace(peek.DataDir)) dataDir = peek.DataDir;
        }
        catch { /* ใช้ค่า default "data" */ }

        var resolved = Path.IsPathRooted(dataDir) ? dataDir : Path.Combine(sidecarDir, dataDir);
        return Path.Combine(resolved, "sidecar.stopped");
    }

    static void StartSidecar(string sidecarExe)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = sidecarExe,
                WorkingDirectory = Path.GetDirectoryName(sidecarExe)!,  // sidecar อ่าน config.json จากโฟลเดอร์ตัวเอง
                UseShellExecute = true,
            });
        }
        catch { /* ลองใหม่รอบหน้า */ }
    }

    static void ClearStopFlag(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ไม่เป็นไร */ }
    }

    static bool IsRunning(string processName)
    {
        try { return Process.GetProcessesByName(processName).Length > 0; }
        catch { return false; }
    }

    static bool TryResolve(string path, string baseDir, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        full = Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);
        return File.Exists(full);
    }

    static void Warn(string msg) =>
        System.Windows.Forms.MessageBox.Show(msg, "Truck Scale Watchdog",
            System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
}
