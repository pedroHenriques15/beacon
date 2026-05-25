using System.Security.Cryptography;
using System.Text;

namespace FinanceHub.Api.Middleware;

public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config, IHostEnvironment env)
{
    private const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/api/auth/google/callback"))
        {
            await next(context);
            return;
        }

        var configuredKey = config["ApiKey"];

        if (string.IsNullOrEmpty(configuredKey))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "ApiKey must be configured in production. Set the ApiKey environment variable.");
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedKey.ToString()),
                Encoding.UTF8.GetBytes(configuredKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or missing API key.");
            return;
        }

        await next(context);
    }
}
