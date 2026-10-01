using System.Data.Common;
using System.Reflection;
using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Health.Queries.GetHealth;

public class GetHealthQueryHandler(AppDbContext db, ILogger<GetHealthQueryHandler> logger)
{
    private static readonly (string Version, string? Commit) Release = ParseRelease(
        typeof(GetHealthQueryHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");

    // Logs only a degraded answer: deploy scripts and monitors call this often.
    public async Task<GetHealthResponse> HandleAsync(CancellationToken ct = default)
    {
        try
        {
            // For SQLite this checks that the file exists. A query would create a missing file.
            if (!await db.Database.CanConnectAsync(ct))
            {
                logger.LogWarning("Health check: no database file at {DataSource}",
                    db.Database.GetDbConnection().DataSource);
                return Respond(GetHealthResponse.Degraded, "database unreachable", newestMigration: null);
            }

            // Reading the migration history opens the file and reads it: the trivial query.
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = db.Database.GetMigrations().Except(applied).ToList();
            if (pending.Count > 0)
            {
                logger.LogWarning("Health check: migrations pending: {Pending}", string.Join(", ", pending));
                return Respond(GetHealthResponse.Degraded, "migrations pending", applied.LastOrDefault());
            }

            return Respond(GetHealthResponse.Ok, reason: null, applied.LastOrDefault());
        }
        catch (DbException ex)
        {
            logger.LogWarning(ex, "Health check: the database cannot be read");
            return Respond(GetHealthResponse.Degraded, "database unreachable", newestMigration: null);
        }
    }

    /// <summary>
    /// Splits the informational version the SDK stamps on the build, "1.0.0+&lt;commit&gt;" when it
    /// builds from a git checkout, into the version and the commit.
    /// </summary>
    internal static (string Version, string? Commit) ParseRelease(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+');
        return plus < 0
            ? (informationalVersion, null)
            : (informationalVersion[..plus], informationalVersion[(plus + 1)..]);
    }

    private static GetHealthResponse Respond(string status, string? reason, string? newestMigration) =>
        new(status, reason, Release.Version, Release.Commit, newestMigration);
}
