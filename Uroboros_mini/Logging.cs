using System.Text;

namespace Uroboros.MiniUploader;

internal interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(Exception ex, string message);
}

internal sealed class FileLogger : ILogger
{
    private const int RetentionDays = 7;
    private readonly object _sync = new();
    private readonly string _logDir;

    public FileLogger(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(_logDir);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(Exception ex, string message) => Write("ERROR", $"{message} | {ex}");

    private void Write(string level, string message)
    {
        var now = DateTime.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (_sync)
        {
            Console.WriteLine(line);
            var path = ResolveLogPath(now);
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(true));
        }
    }

    private string ResolveLogPath(DateTime now)
    {
        var slot = ((int)(now.Date - DateTime.UnixEpoch.Date).TotalDays % RetentionDays) + 1;
        var prefix = $"day{slot}_";
        var desiredName = $"{prefix}{now:yyyyMMdd}.log";
        var desiredPath = Path.Combine(_logDir, desiredName);

        var existing = Directory.GetFiles(_logDir, $"{prefix}*.log");
        foreach (var path in existing)
        {
            if (string.Equals(path, desiredPath, StringComparison.OrdinalIgnoreCase))
                continue;

            File.Delete(path);
        }

        return desiredPath;
    }
}
