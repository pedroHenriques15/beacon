namespace Beacon.Api.Services.Logging;

/// <summary>
/// Minimum levels set in code, since production has no appsettings.json: without them every
/// request and SQL command would be logged at Information. <c>Logging:LogLevel:{category}</c>
/// (<c>Logging__LogLevel__...</c>) overrides any of them.
/// </summary>
public static class LogLevels
{
    public static readonly IReadOnlyList<(string Category, LogLevel Level)> Defaults =
    [
        ("Microsoft", LogLevel.Warning),
        ("Microsoft.EntityFrameworkCore", LogLevel.Warning),
        ("Microsoft.Hosting.Lifetime", LogLevel.Information),
        ("System", LogLevel.Warning),
    ];

    public static void ApplyDefaults(ILoggingBuilder logging, IConfiguration configuration)
    {
        // "Logging:LogLevel:Default", when set, takes precedence over the minimum level.
        logging.SetMinimumLevel(LogLevel.Information);

        // A rule added here would win over the configuration's rule for the same category, so a
        // default is only added where the configuration names none.
        foreach (var (category, level) in Defaults)
        {
            if (configuration[$"Logging:LogLevel:{category}"] is null)
                logging.AddFilter(category, level);
        }
    }
}
