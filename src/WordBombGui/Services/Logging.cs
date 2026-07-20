// Logging provides the application's rotating file logger and the thread-safe
// in-memory log queue consumed by the GUI. Port of logging_utils.py / logging.go.
using System.IO;
using System.Text;

namespace WordBombTool;

public enum LogLevel { Info, Warning, Error }

/// <summary>A minimal size-based rotating file writer (name, name.1 .. name.N).</summary>
public sealed class RotatingFileLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _backups;
    private FileStream _stream;

    public RotatingFileLogger(string path, long maxBytes = 5 * 1024 * 1024, int backups = 3)
    {
        _path = path;
        _maxBytes = maxBytes;
        _backups = backups;
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public void WriteLine(string level, string message)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss,fff");
        var line = $"{ts} - {level} - {message}\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        lock (_lock)
        {
            if (_stream.Length + bytes.Length > _maxBytes) Rotate();
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
    }

    private void Rotate()
    {
        _stream.Dispose();
        for (var i = _backups; i >= 1; i--)
        {
            var src = _path + "." + i;
            var dst = _path + "." + (i + 1);
            if (i == _backups)
            {
                if (File.Exists(src)) File.Delete(src);
                continue;
            }
            if (File.Exists(src)) File.Move(src, dst, overwrite: true);
        }
        if (File.Exists(_path)) File.Move(_path, _path + ".1", overwrite: true);
        _stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public void Dispose()
    {
        lock (_lock) { _stream.Dispose(); }
    }
}

/// <summary>Package-level style static logger, mirroring the Go `logging` package's
/// Infof/Warnf/Errorf free functions.</summary>
public static class AppLog
{
    private static RotatingFileLogger? _logger;

    public static void Setup()
    {
        _logger = new RotatingFileLogger(AppConfig.LogFile);
    }

    public static void Infof(string format, params object[] args) => Write("INFO", format, args);
    public static void Warnf(string format, params object[] args) => Write("WARNING", format, args);
    public static void Errorf(string format, params object[] args) => Write("ERROR", format, args);

    private static void Write(string level, string format, object[] args)
    {
        try
        {
            var msg = args.Length > 0 ? string.Format(format, args) : format;
            _logger?.WriteLine(level, msg);
        }
        catch
        {
            // Never let logging failures crash the app.
        }
    }

    public static void Close() => _logger?.Dispose();
}

public readonly struct LogEntry
{
    public LogEntry(string message, string color) { Message = message; Color = color; }
    public string Message { get; }
    public string Color { get; }
}

/// <summary>A thread-safe, bounded FIFO of colored log lines for the UI.</summary>
public sealed class LogQueue
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _items = new();
    private readonly int _maxSize;

    public LogQueue(int maxSize = 0) => _maxSize = maxSize > 0 ? maxSize : AppConfig.MaxLogQueueSize;

    public void Add(string message, LogLevel level)
    {
        var color = level switch
        {
            LogLevel.Error => Theme.Error,
            LogLevel.Warning => Theme.Warning,
            _ => Theme.LogFg,
        };
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_lock)
        {
            _items.Add(new LogEntry(formatted, color));
            if (_items.Count > _maxSize) _items.RemoveRange(0, _items.Count - _maxSize);
        }
    }

    public List<LogEntry> PopAll()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return new List<LogEntry>();
            var copy = new List<LogEntry>(_items);
            _items.Clear();
            return copy;
        }
    }

    public bool HasMessages()
    {
        lock (_lock) return _items.Count > 0;
    }
}
