using Microsoft.Extensions.Logging;

namespace Beacon.Tests.Services;

/// <summary>Records every event that reaches it, after the logging filters, with its category.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (provider.Entries) provider.Entries.Add((category, logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>A folder under the system's temp folder, removed when disposed.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"beacon-tests-{Guid.NewGuid():N}");

    public TempFolder() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open; the system's temp cleanup takes it.
        }
    }
}
