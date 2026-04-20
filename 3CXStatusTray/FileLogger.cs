using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace _3CXStatusTray;

/// <summary>
/// A single-process file sink for Microsoft.Extensions.Logging that writes
/// nothing by default. The right-click tray menu has "Enable logging" /
/// "Stop logging" items that toggle it on and off at runtime.
///
/// When enabled, it opens a fresh timestamped file under
/// %LocalAppData%\3CXStatusTray\ and appends log lines at Information
/// level or higher. When disabled, the file is closed - no further
/// writes. Exiting the app also closes the file. Nothing is written
/// unless the user explicitly enables it; no logs persist across
/// restarts (unless they enable again and re-produce them).
/// </summary>
internal static class FileLogger
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;

    public static bool IsEnabled
    {
        get { lock (_lock) return _writer != null; }
    }

    public static string? CurrentPath { get; private set; }

    public static void Enable(string path)
    {
        lock (_lock)
        {
            Disable_NoLock();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, append: false) { AutoFlush = true };
            CurrentPath = path;
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [Info] FileLogger: logging enabled");
        }
    }

    public static void Disable()
    {
        lock (_lock) Disable_NoLock();
    }

    private static void Disable_NoLock()
    {
        if (_writer != null)
        {
            try { _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [Info] FileLogger: logging disabled"); } catch { }
            try { _writer.Dispose(); } catch { }
            _writer = null;
        }
        CurrentPath = null;
    }

    internal static void WriteLine(string line)
    {
        lock (_lock)
        {
            _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
    }
}

internal sealed class FileLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Impl(categoryName);
    public void Dispose() => FileLogger.Disable();

    private sealed class Impl : ILogger
    {
        private readonly string _category;
        public Impl(string category) => _category = category;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => FileLogger.IsEnabled && logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var line = $"[{logLevel}] {_category}: {message}";
            if (exception != null) line += $" {exception}";
            FileLogger.WriteLine(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
