namespace Beacon.Api.Features.Logs.Queries.GetLogs;

/// <summary>
/// The newest log entries at <see cref="MinLevel"/> or above (a Serilog level name:
/// Verbose, Debug, Information, Warning, Error, Fatal), from <see cref="From"/> to
/// <see cref="To"/>, whose message, details or source contain <see cref="Search"/>, at most
/// <see cref="Limit"/> of them.
/// </summary>
public record GetLogsQuery(
    string? MinLevel = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Search = null,
    int? Limit = null);
