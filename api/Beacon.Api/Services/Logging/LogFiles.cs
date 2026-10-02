using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;

namespace Beacon.Api.Services.Logging;

/// <summary>
/// Where the API writes its log files (<c>Logs:Path</c>): one JSON object per line (Serilog's
/// compact format, CLEF: the message template and its properties), a file a day,
/// files older than <c>Logs:Keep</c> days removed. <see cref="Directory"/> is null when file
/// logging is off (no path, or one that can't be written), and the console is all there is.
/// </summary>
public sealed class LogFiles
{
    public const int DefaultKeepDays = 14;

    /// <summary>Each file is <c>beacon-yyyyMMdd.json</c>; a day past the size cap adds <c>_001</c> and on.</summary>
    public const string Pattern = "beacon-*.json";

    private const long FileSizeLimitBytes = 100L * 1024 * 1024;

    public LogFiles(string? directory, int keepDays)
    {
        Directory = directory;
        KeepDays = keepDays;
    }

    public string? Directory { get; }
    public int KeepDays { get; }

    /// <summary>
    /// Reads the settings and makes sure the folder can be written. Never throws: a server whose
    /// environment predates the setting must still start, so a problem is returned as a warning
    /// to log once and file logging stays off.
    /// </summary>
    public static (LogFiles Files, string? Warning) Resolve(IConfiguration configuration, string contentRoot)
    {
        var keep = Math.Max(1, configuration.GetValue("Logs:Keep", DefaultKeepDays));
        var path = configuration["Logs:Path"];
        if (string.IsNullOrWhiteSpace(path))
            return (new LogFiles(null, keep), "Logs:Path is not set: logging to the console only.");

        try
        {
            var directory = Path.GetFullPath(path, contentRoot);
            System.IO.Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return (new LogFiles(directory, keep), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return (new LogFiles(null, keep),
                $"Logs:Path '{path}' can't be written ({ex.Message}): logging to the console only.");
        }
    }

    /// <summary>
    /// Adds the files as a provider beside the console. Not through Serilog's <c>AddSerilog</c>,
    /// which also adds a Trace rule for its provider that outranks every <c>Logging:LogLevel</c>
    /// rule: the files would get every framework and SQL line. Registered through a factory, so
    /// the container disposes it on shutdown and the last lines are flushed. Does nothing when
    /// file logging is off.
    /// </summary>
    public void AddTo(ILoggingBuilder logging)
    {
        if (Directory is not null)
            logging.Services.AddSingleton<ILoggerProvider>(_ => new SerilogLoggerProvider(CreateLogger(), dispose: true));
    }

    /// <summary>The Serilog logger behind the file provider. Levels are filtered before it, by <see cref="LogLevels"/>.</summary>
    public Serilog.ILogger CreateLogger()
    {
        if (Directory is null) throw new InvalidOperationException("File logging is off.");

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(Directory, "beacon-.json"),
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(KeepDays),
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();
    }

    /// <summary>The log files, newest first (the date is in the name, and a size roll sorts after its day).</summary>
    public IEnumerable<string> NewestFirst() =>
        Directory is null || !System.IO.Directory.Exists(Directory)
            ? []
            : System.IO.Directory.GetFiles(Directory, Pattern).OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
}
