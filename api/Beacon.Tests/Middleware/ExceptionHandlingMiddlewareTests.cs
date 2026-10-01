using System.Text.Json;
using Beacon.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, JsonElement Body)> RunAsync(Exception toThrow)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw toThrow,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, JsonDocument.Parse(body).RootElement);
    }

    [Fact]
    public async Task ArgumentException_MapsTo400ProblemDetails()
    {
        var (status, body) = await RunAsync(new ArgumentException("Name is required."));

        Assert.Equal(400, status);
        Assert.Equal("Name is required.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task NotSupportedException_MapsTo400ProblemDetails()
    {
        var (status, body) = await RunAsync(new NotSupportedException("Only EUR statements are supported."));

        Assert.Equal(400, status);
        Assert.Equal("Only EUR statements are supported.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnhandledException_MapsTo500WithoutLeakingDetails()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("secret internal state /srv/path"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task CancelledRequest_DoesNotSurfaceAs200()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext { RequestAborted = new CancellationToken(true) };
        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task PassesThrough_WhenNoException()
    {
        var called = false;
        var middleware = new ExceptionHandlingMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.True(called);
    }
}
