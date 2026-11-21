using System.Text;

namespace OpenCodePhoneProxy.Services;

public sealed class Logger
{
    private const long MaxBytes = 1_048_576;
    private readonly object _gate = new();
    private readonly string _logPath;

    public Logger(string root)
    {
        var logDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "app.log");
    }

    public string LogPath => _logPath;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public string Tail(int lines)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_logPath))
                {
                    return "";
                }

                var all = File.ReadAllLines(_logPath, Encoding.UTF8);
                return string.Join(Environment.NewLine, all.TakeLast(Math.Max(1, lines)));
            }
        }
        catch (Exception ex)
        {
            return "Could not read the log: " + ex.Message;
        }
    }

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                RollIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private void RollIfNeeded()
    {
        var info = new FileInfo(_logPath);
        if (!info.Exists || info.Length <= MaxBytes)
        {
            return;
        }

        var previous = _logPath + ".1";
        try
        {
            File.Delete(previous);
            File.Move(_logPath, previous);
        }
        catch
        {
            // If rolling fails, keep appending to the current file.
        }
    }
}
