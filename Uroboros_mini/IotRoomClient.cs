using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uroboros.MiniUploader;

internal sealed class IotRoomClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _deviceName;
    private readonly string _deviceType;
    private readonly int _pollIntervalSeconds;
    private readonly ILogger _log;

    private string? _sessionKey;
    private readonly SemaphoreSlim _joinLock = new(1, 1);

    public bool IsEnabled { get; }

    public IotRoomClient(MiniUploaderConfig config, ILogger log)
    {
        _baseUrl = config.IotServerUrl.TrimEnd('/');
        _deviceName = config.IotDeviceName;
        _deviceType = config.IotDeviceType;
        _pollIntervalSeconds = Math.Max(1, config.IotPollIntervalSeconds);
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        IsEnabled = config.IotEnabled;
    }

    public async Task RunHeartbeatAsync(CancellationToken ct)
    {
        if (!IsEnabled) return;

        await JoinWithRetryAsync(ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), ct).ConfigureAwait(false);
                await PollAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Warn($"[IoT] Heartbeat error: {ex.Message}");
            }
        }
    }

    public async Task SendDataAsync(IotStatusPayload payload, CancellationToken ct)
    {
        if (!IsEnabled || _sessionKey is null) return;

        try
        {
            var body = new { key = _sessionKey, data = payload };
            using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/iot/room/data", body, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                _log.Info("[IoT] Data sent");
            else
                _log.Warn($"[IoT] Data send failed: HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            _log.Warn($"[IoT] SendData error: {ex.Message}");
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        if (_sessionKey is null) return;

        try
        {
            var body = new { key = _sessionKey };
            using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/iot/room/poll", body, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _log.Warn($"[IoT] Poll HTTP {(int)resp.StatusCode}");
                return;
            }

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            var status = json.TryGetProperty("status", out var s) ? s.GetString() : null;

            if (string.Equals(status, "reconnect", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info("[IoT] Session expired — rejoining...");
                _sessionKey = null;
                await JoinWithRetryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"[IoT] Poll error: {ex.Message}");
        }
    }

    private async Task JoinWithRetryAsync(CancellationToken ct)
    {
        await _joinLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_sessionKey is not null) return;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var body = new { deviceName = _deviceName, deviceType = _deviceType };
                    using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/iot/room/join", body, ct).ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        _log.Warn($"[IoT] Join failed: HTTP {(int)resp.StatusCode} — retry in 10s");
                        await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                        continue;
                    }

                    var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
                    var key = json.TryGetProperty("key", out var k) ? k.GetString() : null;

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        _log.Warn("[IoT] Join response missing key — retry in 10s");
                        await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                        continue;
                    }

                    _sessionKey = key;
                    _log.Info($"[IoT] Joined room as {_deviceName} (key={key})");
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Warn($"[IoT] Join error: {ex.Message} — retry in 10s");
                    await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _joinLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        _joinLock.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}

internal sealed class IotStatusPayload
{
    [JsonPropertyName("lastRunAt")]
    public string? LastRunAt { get; init; }
}
