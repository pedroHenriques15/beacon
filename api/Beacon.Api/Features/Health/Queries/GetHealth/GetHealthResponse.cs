namespace Beacon.Api.Features.Health.Queries.GetHealth;

/// <summary>
/// <see cref="Status"/> is <see cref="Ok"/> or <see cref="Degraded"/>, with a short
/// <see cref="Reason"/> when degraded. <see cref="Commit"/> is null for a build made outside a git
/// checkout. <see cref="NewestMigration"/> is the newest migration applied to the database, null
/// when the database cannot be read.
/// </summary>
public record GetHealthResponse(string Status, string? Reason, string Version, string? Commit, string? NewestMigration)
{
    public const string Ok = "ok";
    public const string Degraded = "degraded";
}
