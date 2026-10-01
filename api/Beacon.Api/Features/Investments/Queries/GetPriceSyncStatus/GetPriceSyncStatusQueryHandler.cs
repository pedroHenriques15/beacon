using Beacon.Api.Services.Pricing;

namespace Beacon.Api.Features.Investments.Queries.GetPriceSyncStatus;

/// <summary>Lets the client offer a price sync only where one can run.</summary>
public class GetPriceSyncStatusQueryHandler(IConfiguration configuration)
{
    public GetPriceSyncStatusResponse Handle(GetPriceSyncStatusQuery query) =>
        new(PriceSyncSettings.Enabled(configuration));
}
