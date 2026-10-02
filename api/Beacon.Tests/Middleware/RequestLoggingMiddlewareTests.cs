using Beacon.Api.Middleware;
using Beacon.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Beacon.Tests.Middleware;

public class RequestLoggingMiddlewareTests
{
    private static async Task<(LogLevel Level, string Message)> RunAsync(string method, string path, string query, int status)
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = status;
                return Task.CompletedTask;
            },
            factory.CreateLogger<RequestLoggingMiddleware>());

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        await middleware.InvokeAsync(context);

        var (_, level, message) = Assert.Single(provider.Entries);
        return (level, message);
    }

    [Fact]
    public async Task OneLine_WithMethodPathStatusAndDuration_ButNoQueryString()
    {
        var (level, message) = await RunAsync("GET", "/api/transactions", "?search=pharmacy", 200);

        Assert.Equal(LogLevel.Information, level);
        Assert.Matches(@"^HTTP GET /api/transactions responded 200 in \d+ ms$", message);
        Assert.DoesNotContain("pharmacy", message);
    }

    [Fact]
    public async Task ServerErrors_LogAtError()
    {
        var (level, _) = await RunAsync("POST", "/api/upload", "", 500);

        Assert.Equal(LogLevel.Error, level);
    }
}
