namespace Beacon.Api.Features.Logs.Queries.GetLogs;

/// <summary>
/// <see cref="Enabled"/> is false when the server writes no log files (no <c>Logs:Path</c>).
/// <see cref="More"/> says older entries matched beyond the limit.
/// </summary>
public record GetLogsResponse(bool Enabled, List<LogEntryResponse> Entries, bool More);

/// <summary>
/// One log event. <see cref="Source"/> is the logger's category (the class that logged it, or
/// <c>Beacon.Client</c> for errors the web client reported); <see cref="Details"/> is the
/// exception, or the stack of a client error.
/// </summary>
public record LogEntryResponse(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Source,
    string? Details);
