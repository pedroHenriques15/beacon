namespace Beacon.Api.Features.Logs.Commands.LogClientError;

/// <summary>
/// Logs a client error at Error under the <see cref="Category"/> category, its stack as the
/// <see cref="StackProperty"/> property. Each field is cut to its cap, so a client can't fill the
/// log files; the controller caps the request body and the rate.
/// </summary>
public class LogClientErrorCommandHandler(ILoggerFactory loggerFactory)
{
    public const string Category = "Beacon.Client";
    public const string StackProperty = "ClientStack";

    public const int MaxMessageLength = 1000;
    public const int MaxStackLength = 8000;
    public const int MaxRouteLength = 300;

    private readonly ILogger _logger = loggerFactory.CreateLogger(Category);

    public void Handle(LogClientErrorCommand command)
    {
        var message = Cap(command.Message, MaxMessageLength) ?? "(no message)";
        var route = Cap(command.Route, MaxRouteLength) ?? "(unknown route)";
        var stack = Cap(command.Stack, MaxStackLength);

        using (stack is null ? null : _logger.BeginScope(new Dictionary<string, object> { [StackProperty] = stack }))
        {
            _logger.LogError("Client error on {Route}: {Message}", route, message);
        }
    }

    private static string? Cap(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
