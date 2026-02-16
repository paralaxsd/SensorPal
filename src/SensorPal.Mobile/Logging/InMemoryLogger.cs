using Microsoft.Extensions.Logging;

namespace SensorPal.Mobile.Logging;

public record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message);

public sealed class InMemoryLogStore
{
    const int MaxEntries = 200;

    readonly List<LogEntry> _entries = new(MaxEntries);

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_entries) return _entries.ToList(); }
    }

    public event Action? Changed;

    public void Add(LogEntry entry)
    {
        lock (_entries)
        {
            if (_entries.Count >= MaxEntries)
                _entries.RemoveAt(0);
            _entries.Add(entry);
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_entries) _entries.Clear();
        Changed?.Invoke();
    }
}

sealed class InMemoryLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(store, categoryName);
    public void Dispose() { }
}

sealed class InMemoryLogger(InMemoryLogStore store, string category) : ILogger
{
    readonly string _shortCategory = category.Contains('.')
        ? category[(category.LastIndexOf('.') + 1)..]
        : category;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        if (exception is not null)
            message = $"{message}: {exception.Message}";
        store.Add(new LogEntry(DateTimeOffset.Now, logLevel, _shortCategory, message));
    }
}
