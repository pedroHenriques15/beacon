using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Beacon.Api.Services.Logging;

/// <summary>
/// The rate limit on reported client errors: a page stuck in an error loop must not fill the log
/// files. One window for everyone, since the API has one user.
/// </summary>
public static class ClientErrorRateLimit
{
    public const string Policy = "client-errors";
    public const int PermitsPerMinute = 30;

    public static IServiceCollection AddClientErrorRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(Policy, window =>
            {
                window.PermitLimit = PermitsPerMinute;
                window.Window = TimeSpan.FromMinutes(1);
                window.QueueLimit = 0;
                window.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });
}
