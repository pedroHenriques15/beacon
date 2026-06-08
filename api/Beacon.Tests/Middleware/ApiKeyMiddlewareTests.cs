using Beacon.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Beacon.Tests.Middleware;

public class ApiKeyMiddlewareTests
{
    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ApiKeyMiddleware CreateMiddleware(
        RequestDelegate next,
        string? configuredKey,
        bool isDevelopment = true)
    {
        var dict = new Dictionary<string, string?>();
        if (configuredKey is not null)
            dict["ApiKey"] = configuredKey;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        var env = new FakeHostEnvironment(isDevelopment ? "Development" : "Production");
        return new ApiKeyMiddleware(next, config, env);
    }

    private static DefaultHttpContext BuildContext(string path, string? providedKey = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (providedKey is not null)
            ctx.Request.Headers["X-Api-Key"] = providedKey;
        return ctx;
    }

    [Fact]
    public async Task NoConfiguredKey_AlwaysCallsNext()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, null);
        var ctx         = BuildContext("/api/statements");

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task NoConfiguredKey_InProduction_Throws()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask, null, isDevelopment: false);
        var ctx        = BuildContext("/api/statements");

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(ctx));
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task SwaggerPath_AlwaysCallsNext(string path)
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "secret");
        var ctx         = BuildContext(path);
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ValidKey_CallsNext()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "my-secret");
        var ctx         = BuildContext("/api/statements", "my-secret");

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "my-secret");
        var ctx         = BuildContext("/api/statements");

        await middleware.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "my-secret");
        var ctx         = BuildContext("/api/statements", "wrong-key");

        await middleware.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_WritesErrorMessageToBody()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask, "secret");
        var ctx        = BuildContext("/api/statements");

        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        Assert.Contains("Invalid or missing API key", body);
    }

    [Fact]
    public async Task EmptyConfiguredKey_AlwaysCallsNext()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "");
        var ctx         = BuildContext("/api/statements");

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GoogleCallbackPath_BypassesApiKey()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "secret");
        var ctx         = BuildContext("/api/auth/google/callback");

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task GoogleCallbackSubPath_RequiresApiKey()
    {
        bool nextCalled = false;
        var middleware  = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, "secret");
        var ctx         = BuildContext("/api/auth/google/callback/extra");

        await middleware.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }
}
