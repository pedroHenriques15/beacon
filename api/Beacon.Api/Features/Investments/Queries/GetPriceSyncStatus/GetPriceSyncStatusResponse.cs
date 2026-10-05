namespace Beacon.Api.Features.Investments.Queries.GetPriceSyncStatus;

/// <summary>Whether this server syncs prices (<c>Prices:Enabled</c>); the demo doesn't.</summary>
public record GetPriceSyncStatusResponse(bool Enabled);
