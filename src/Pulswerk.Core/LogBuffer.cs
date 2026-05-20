// LogBuffer.cs – Thread-safe ring buffer + global logger for all Pulswerk components
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Pulswerk.Core
{
    /// <summary>
    /// A single log entry captured from the application.
    /// </summary>
    public sealed class LogEntry
    {
        public DateTime Timestamp { get; }
        public LogSeverity Severity { get; }
        public string Message { get; }
        public string Source { get; }

        public LogEntry(DateTime timestamp, LogSeverity severity, string message, string source = "")
        {
            Timestamp = timestamp;
            Severity = severity;
            Message = message;
            Source = source;
        }
    }

    /// <summary>Log severity levels.</summary>
    public enum LogSeverity
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Thread-safe circular ring buffer that captures console log entries.
    /// Bounded to a maximum size; oldest entries are evicted when full.
    /// </summary>
    public sealed class LogBuffer
    {
        private readonly LogEntry[] _debugBuffer;
        private readonly LogEntry[] _importantBuffer;
        
        private readonly int _capacity;
        private int _debugHead = 0;
        private int _debugCount = 0;
        private int _importantHead = 0;
        private int _importantCount = 0;

        /// <summary>Thread-safety lock for all public operations.</summary>
        private readonly object _lock = new object();

        public LogBuffer(int capacity = 5000)
        {
            _capacity = capacity > 0 ? capacity : 5000;
            _debugBuffer = new LogEntry[_capacity];
            _importantBuffer = new LogEntry[_capacity];
        }

        /// <summary>
        /// Adds a log entry to the buffer. Thread-safe.
        /// Important messages and debug messages use separate ring buffers to prevent important messages from being flushed.
        /// </summary>
        public void Add(LogEntry entry)
        {
            if (entry == null) return;

            lock (_lock)
            {
                if (entry.Severity == LogSeverity.Debug)
                {
                    _debugBuffer[_debugHead] = entry;
                    _debugHead = (_debugHead + 1) % _capacity;
                    if (_debugCount < _capacity) _debugCount++;
                }
                else
                {
                    _importantBuffer[_importantHead] = entry;
                    _importantHead = (_importantHead + 1) % _capacity;
                    if (_importantCount < _capacity) _importantCount++;
                }
            }
        }

        /// <summary>
        /// Returns the last <paramref name="count"/> log entries in chronological order (oldest first).
        /// </summary>
        public List<LogEntry> GetLatest(int count = 200)
        {
            lock (_lock)
            {
                if (count <= 0) count = 200;

                var all = new List<LogEntry>();
                
                int dCount = Math.Min(count, _debugCount);
                int dStart = (_debugHead - dCount + _capacity) % _capacity;
                for (int i = 0; i < dCount; i++)
                {
                    all.Add(_debugBuffer[(dStart + i) % _capacity]);
                }

                int iCount = Math.Min(count, _importantCount);
                int iStart = (_importantHead - iCount + _capacity) % _capacity;
                for (int i = 0; i < iCount; i++)
                {
                    all.Add(_importantBuffer[(iStart + i) % _capacity]);
                }

                all.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                if (all.Count > count)
                {
                    all.RemoveRange(0, all.Count - count);
                }
                
                return all;
            }
        }

        /// <summary>
        /// Returns all entries currently in the buffer in chronological order.
        /// </summary>
        public List<LogEntry> GetAll() => GetLatest(int.MaxValue);

        /// <summary>Total capacity of EACH buffer.</summary>
        public int Capacity => _capacity;

        /// <summary>Number of entries currently stored.</summary>
        public int Count
        {
            get
            {
                lock (_lock) return _debugCount + _importantCount;
            }
        }
    }

    /// <summary>
    /// A lightweight logger that writes to both the Console and the LogBuffer.
    /// Replace all Console.WriteLine / Console.Error.WriteLine calls with this.
    /// </summary>
    public sealed class ConsoleLogger
    {
        private readonly LogBuffer _buffer;

        public ConsoleLogger(LogBuffer buffer)
        {
            _buffer = buffer ?? new LogBuffer(5000);
        }

        /// <summary>Log a debug message (only written to buffer + stdout, no colour).</summary>
        public void Debug(string message, string source = "")
        {
            var entry = new LogEntry(DateTime.UtcNow, LogSeverity.Debug, message, source);
            _buffer.Add(entry);
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  DBG  {message}");
        }

        /// <summary>Log an informational message.</summary>
        public void Info(string message, string source = "")
        {
            var entry = new LogEntry(DateTime.UtcNow, LogSeverity.Info, message, source);
            _buffer.Add(entry);
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  INF  {message}");
        }

        /// <summary>Log a warning message.</summary>
        public void Warning(string message, string source = "")
        {
            var entry = new LogEntry(DateTime.UtcNow, LogSeverity.Warning, message, source);
            _buffer.Add(entry);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  WRN  {message}");
            Console.ResetColor();
        }

        /// <summary>Log an error message.</summary>
        public void Error(string message, string source = "")
        {
            var entry = new LogEntry(DateTime.UtcNow, LogSeverity.Error, message, source);
            _buffer.Add(entry);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  ERR  {message}");
            Console.ResetColor();
        }

        /// <summary>Access the underlying log buffer for the dashboard.</summary>
        public LogBuffer Buffer => _buffer;
    }

    /// <summary>
    /// Global static logging facade.  Initialised once from Program.cs via
    /// <c>Log.Init(logger)</c>, then used everywhere as <c>Log.Info(...)</c>.
    /// Before Init() is called, messages fall through to plain Console output.
    /// </summary>
    public static class Log
    {
        private static ConsoleLogger? _instance;

        /// <summary>Wire up the global logger (call once from Program.Main).</summary>
        public static void Init(ConsoleLogger logger) => _instance = logger;

        /// <summary>The underlying ConsoleLogger, or null before Init().</summary>
        public static ConsoleLogger? Instance => _instance;

        // ── Convenience methods ──────────────────────────────────────────────

        public static void Debug(string message, string source = "")
        {
            if (_instance != null) _instance.Debug(message, source);
            else Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  DBG  {message}");
        }

        public static void Info(string message, string source = "")
        {
            if (_instance != null) _instance.Info(message, source);
            else Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  INF  {message}");
        }

        public static void Warning(string message, string source = "")
        {
            if (_instance != null) _instance.Warning(message, source);
            else Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  WRN  {message}");
        }

        public static void Error(string message, string source = "")
        {
            if (_instance != null) _instance.Error(message, source);
            else Console.Error.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  ERR  {message}");
        }
    }
}
