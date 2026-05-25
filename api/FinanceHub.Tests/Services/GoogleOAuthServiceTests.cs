using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using FinanceHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceHub.Tests.Services;

public class GoogleOAuthServiceTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static IMemoryCache CreateCache() =>
        new MemoryCache(Options.Create(new MemoryCacheOptions()));

    private static GoogleOAuthService CreateService(AppDbContext db, IMemoryCache cache,
        IHttpClientFactory? factory = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleServices:ClientId"]     = "test-client-id",
                ["GoogleServices:ClientSecret"] = "test-client-secret",
                ["GoogleServices:RedirectUri"]  = "http://localhost/callback",
                ["GoogleServices:FrontendUrl"]  = "http://localhost:4200",
            })
            .Build();

        factory ??= new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"test-access","refresh_token":"test-refresh","expires_in":3600}"""));

        return new GoogleOAuthService(factory, config, db, cache);
    }

    // --- GetAuthorizationUrl ---

    [Fact]
    public void GetAuthorizationUrl_ReturnsGoogleUrl_WithRequiredParams()
    {
        using var db    = CreateDb(nameof(GetAuthorizationUrl_ReturnsGoogleUrl_WithRequiredParams));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        var url = svc.GetAuthorizationUrl();

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("client_id=test-client-id",        url);
        Assert.Contains("response_type=code",              url);
        Assert.Contains("access_type=offline",             url);
        Assert.Contains("prompt=consent",                  url);
        Assert.Contains("state=",                          url);
    }

    [Fact]
    public void GetAuthorizationUrl_StoresStateInCache()
    {
        using var db    = CreateDb(nameof(GetAuthorizationUrl_StoresStateInCache));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        svc.GetAuthorizationUrl();

        Assert.True(cache.TryGetValue("google_oauth_state", out string? state));
        Assert.False(string.IsNullOrEmpty(state));
    }

    // --- ExchangeCodeAsync: state verification ---

    [Fact]
    public async Task ExchangeCodeAsync_InvalidState_Throws()
    {
        using var db    = CreateDb(nameof(ExchangeCodeAsync_InvalidState_Throws));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code", "wrong-state"));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ValidState_StoresToken()
    {
        using var db    = CreateDb(nameof(ExchangeCodeAsync_ValidState_StoresToken));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await svc.ExchangeCodeAsync("auth-code", state!);

        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal("test-access",  token.AccessToken);
        Assert.Equal("test-refresh", token.RefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ConsumesState_SecondCallFails()
    {
        using var db    = CreateDb(nameof(ExchangeCodeAsync_ConsumesState_SecondCallFails));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);
        await svc.ExchangeCodeAsync("auth-code", state!);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code-2", state!));
    }

    [Fact]
    public async Task ExchangeCodeAsync_NoRefreshToken_Throws()
    {
        using var db    = CreateDb(nameof(ExchangeCodeAsync_NoRefreshToken_Throws));
        using var cache = CreateCache();
        var factory     = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"test-access","expires_in":3600}"""));
        var svc         = CreateService(db, cache, factory);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code", state!));
    }

    // --- GetStatusAsync ---

    [Fact]
    public async Task GetStatusAsync_NoToken_ReturnsDisconnected()
    {
        using var db    = CreateDb(nameof(GetStatusAsync_NoToken_ReturnsDisconnected));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        var status = await svc.GetStatusAsync();

        Assert.False(status.Connected);
        Assert.Null(status.ExpiresAt);
        Assert.Null(status.ConnectedAt);
    }

    [Fact]
    public async Task GetStatusAsync_WithToken_ReturnsConnected()
    {
        using var db    = CreateDb(nameof(GetStatusAsync_WithToken_ReturnsConnected));
        var now         = DateTime.UtcNow;
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "tok",
            RefreshToken = "ref",
            ExpiresAt    = now.AddHours(1),
            ConnectedAt  = now,
        });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        var status = await svc.GetStatusAsync();

        Assert.True(status.Connected);
        Assert.NotNull(status.ExpiresAt);
        Assert.NotNull(status.ConnectedAt);
    }

    // --- DisconnectAsync ---

    [Fact]
    public async Task DisconnectAsync_RemovesAllTokens()
    {
        using var db = CreateDb(nameof(DisconnectAsync_RemovesAllTokens));
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "tok",
            RefreshToken = "ref",
            ExpiresAt    = DateTime.UtcNow.AddHours(1),
            ConnectedAt  = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        await svc.DisconnectAsync();

        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());
    }

    // --- GetValidAccessTokenAsync with stale token / refresh failure ---

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshFails_ReturnsNull_AndDisconnects()
    {
        using var db = CreateDb(nameof(GetValidAccessTokenAsync_RefreshFails_ReturnsNull_AndDisconnects));
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "old-token",
            RefreshToken = "bad-refresh",
            ExpiresAt    = DateTime.UtcNow.AddMinutes(-5),
            ConnectedAt  = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.BadRequest, ""));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Null(result);
        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());
    }

    // --- GetValidAccessTokenAsync: valid token fast path ---

    [Fact]
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsToken_WithoutHttpCall()
    {
        using var db = CreateDb(nameof(GetValidAccessTokenAsync_ValidToken_ReturnsToken_WithoutHttpCall));
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "still-valid",
            RefreshToken = "refresh",
            ExpiresAt    = DateTime.UtcNow.AddHours(1),
            ConnectedAt  = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new ThrowingHttpMessageHandler());
        using var cache = CreateCache();
        var svc         = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Equal("still-valid", result);
    }

    // --- Disconnect then reconnect ---

    [Fact]
    public async Task ConnectDisconnectReconnect_StoresNewTokenWithIdOne()
    {
        using var db    = CreateDb(nameof(ConnectDisconnectReconnect_StoresNewTokenWithIdOne));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state1);
        await svc.ExchangeCodeAsync("code-1", state1!);

        await svc.DisconnectAsync();
        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state2);
        await svc.ExchangeCodeAsync("code-2", state2!);

        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal(1, token.Id);
        Assert.Equal("test-access", token.AccessToken);
    }

    // --- Helpers ---

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No HTTP calls expected for a valid token.");
    }

    private sealed class FakeHttpMessageHandler(System.Net.HttpStatusCode status, string body)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
