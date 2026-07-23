using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Beacon.Tests.Services;

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

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshInvalidGrant_ReturnsNull_AndDisconnects()
    {
        using var db = CreateDb(nameof(GetValidAccessTokenAsync_RefreshInvalidGrant_ReturnsNull_AndDisconnects));
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
            System.Net.HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}"""));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Null(result);
        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshTransientError_ReturnsNull_KeepsTokens()
    {
        using var db = CreateDb(nameof(GetValidAccessTokenAsync_RefreshTransientError_ReturnsNull_KeepsTokens));
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "old-token",
            RefreshToken = "good-refresh",
            ExpiresAt    = DateTime.UtcNow.AddMinutes(-5),
            ConnectedAt  = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.ServiceUnavailable, ""));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Null(result);
        Assert.Single(await db.GoogleOAuthTokens.ToListAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_StaleToken_RefreshSucceeds_ReturnsNewToken()
    {
        using var db = CreateDb(nameof(GetValidAccessTokenAsync_StaleToken_RefreshSucceeds_ReturnsNewToken));
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "old-token",
            RefreshToken = "good-refresh",
            ExpiresAt    = DateTime.UtcNow.AddMinutes(-5),
            ConnectedAt  = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"new-token","expires_in":3600}"""));
        using var cache = CreateCache();
        var svc         = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Equal("new-token", result);
        var stored = await db.GoogleOAuthTokens.FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("new-token", stored.AccessToken);
    }

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

    [Fact]
    public async Task ExchangeCodeAsync_ReconnectWithoutDisconnect_UpdatesTokenAndPreservesRefreshToken()
    {
        using var db    = CreateDb(nameof(ExchangeCodeAsync_ReconnectWithoutDisconnect_UpdatesTokenAndPreservesRefreshToken));
        using var cache = CreateCache();

        var svc = CreateService(db, cache);
        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state1);
        await svc.ExchangeCodeAsync("code-1", state1!);

        var factory2 = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"new-access","expires_in":3600}"""));
        var svc2 = CreateService(db, cache, factory2);
        svc2.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state2);
        await svc2.ExchangeCodeAsync("code-2", state2!);

        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal("new-access",   token.AccessToken);
        Assert.Equal("test-refresh", token.RefreshToken);
        Assert.Equal(1,              token.Id);
    }

    [Fact]
    public async Task ExchangeCodeAsync_GoogleReturnsError_ThrowsHttpRequestException()
    {
        using var db    = CreateDb(nameof(ExchangeCodeAsync_GoogleReturnsError_ThrowsHttpRequestException));
        using var cache = CreateCache();
        var factory     = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var svc         = CreateService(db, cache, factory);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.ExchangeCodeAsync("bad-code", state!));

        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());
    }

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

}
