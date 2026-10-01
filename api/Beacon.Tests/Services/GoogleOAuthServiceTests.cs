using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Beacon.Tests.Services;

public class GoogleOAuthServiceTests : IDisposable
{
    private const string InvalidGrant =
        """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""";

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private AppDbContext CreateDb() => _database.CreateContext();

    private static IMemoryCache CreateCache() =>
        new MemoryCache(Options.Create(new MemoryCacheOptions()));

    private static async Task SeedExpiredTokenAsync(AppDbContext db)
    {
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "old-token",
            RefreshToken = "good-refresh",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ConnectedAt = DateTime.UtcNow.AddDays(-30),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The row a rejected refresh token leaves behind: both tokens blank.</summary>
    private static async Task SeedRejectedTokenAsync(AppDbContext db)
    {
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "",
            RefreshToken = "",
            ExpiresAt = DateTime.UtcNow.AddDays(-8),
            ConnectedAt = DateTime.UtcNow.AddDays(-30),
        });
        await db.SaveChangesAsync();
    }

    private static GoogleOAuthService CreateService(AppDbContext db, IMemoryCache cache,
        IHttpClientFactory? factory = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleServices:ClientId"] = "test-client-id",
                ["GoogleServices:ClientSecret"] = "test-client-secret",
                ["GoogleServices:RedirectUri"] = "http://localhost/callback",
                ["GoogleServices:FrontendUrl"] = "http://localhost:4200",
            })
            .Build();

        factory ??= new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"test-access","refresh_token":"test-refresh","expires_in":3600}"""));

        return new GoogleOAuthService(factory, config, db, cache, NullLogger<GoogleOAuthService>.Instance);
    }

    [Fact]
    public void GetAuthorizationUrl_ReturnsGoogleUrl_WithRequiredParams()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        var url = svc.GetAuthorizationUrl();

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("client_id=test-client-id", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
        Assert.Contains("state=", url);
    }

    [Fact]
    public void GetAuthorizationUrl_StoresStateInCache()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        svc.GetAuthorizationUrl();

        Assert.True(cache.TryGetValue("google_oauth_state", out string? state));
        Assert.False(string.IsNullOrEmpty(state));
    }

    [Fact]
    public async Task ExchangeCodeAsync_InvalidState_Throws()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code", "wrong-state"));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ValidState_StoresToken()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await svc.ExchangeCodeAsync("auth-code", state!);

        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal("test-access", token.AccessToken);
        Assert.Equal("test-refresh", token.RefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ConsumesState_SecondCallFails()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);
        await svc.ExchangeCodeAsync("auth-code", state!);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code-2", state!));
    }

    [Fact]
    public async Task ExchangeCodeAsync_NoRefreshToken_Throws()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"test-access","expires_in":3600}"""));
        var svc = CreateService(db, cache, factory);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code", state!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusChecks_LogNoEfQueryWarning(bool connected)
    {
        // EF warns when it compiles a query, once per internal service provider: without caching
        // those, each case compiles the token query, and would warn, on its own.
        var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
        var options = new DbContextOptionsBuilder<AppDbContext>(_database.Options)
            .UseLoggerFactory(loggerFactory).EnableServiceProviderCaching(false).Options;
        await using var db = new AppDbContext(options);
        if (connected)
        {
            db.GoogleOAuthTokens.Add(new GoogleOAuthToken
            {
                Id = GoogleOAuthToken.SingletonId,
                AccessToken = "token",
                RefreshToken = "refresh",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                ConnectedAt = DateTime.UtcNow.AddDays(-1),
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        using var cache = CreateCache();

        await CreateService(db, cache).GetStatusAsync();

        Assert.DoesNotContain(provider.Entries, e =>
            e.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            && e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task GetStatusAsync_NoToken_ReturnsDisconnected()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        var status = await svc.GetStatusAsync();

        Assert.Equal(GoogleConnectionState.NotConnected, status.State);
        Assert.False(status.Connected);
        Assert.Null(status.ExpiresAt);
        Assert.Null(status.ConnectedAt);
    }

    [Fact]
    public async Task GetStatusAsync_ValidToken_ReturnsConnected_WithoutHttpCall()
    {
        using var db = CreateDb();
        var now = DateTime.UtcNow;
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "tok",
            RefreshToken = "ref",
            ExpiresAt = now.AddHours(1),
            ConnectedAt = now,
        });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var svc = CreateService(db, cache, new FakeHttpClientFactory(new ThrowingHttpMessageHandler()));

        var status = await svc.GetStatusAsync();

        Assert.Equal(GoogleConnectionState.Connected, status.State);
        Assert.True(status.Connected);
        Assert.NotNull(status.ExpiresAt);
        Assert.NotNull(status.ConnectedAt);
    }

    [Fact]
    public async Task GetStatusAsync_ExpiredToken_RefreshSucceeds_ReturnsConnected()
    {
        using var db = CreateDb();
        await SeedExpiredTokenAsync(db);
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK, """{"access_token":"new-token","expires_in":3600}"""));
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        var status = await svc.GetStatusAsync();

        Assert.Equal(GoogleConnectionState.Connected, status.State);
        Assert.True(status.ExpiresAt > DateTime.UtcNow);
        using var check = CreateDb();
        Assert.Equal("new-token", (await check.GoogleOAuthTokens.SingleAsync()).AccessToken);
    }

    [Fact]
    public async Task GetStatusAsync_RefreshTokenRejected_ReturnsReconnectRequired_AndBlanksTokens()
    {
        using var db = CreateDb();
        await SeedExpiredTokenAsync(db);
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.BadRequest, InvalidGrant));
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        var status = await svc.GetStatusAsync();

        Assert.Equal(GoogleConnectionState.ReconnectRequired, status.State);
        Assert.False(status.Connected);
        Assert.NotNull(status.ConnectedAt);
        using var check = CreateDb();
        var stored = await check.GoogleOAuthTokens.SingleAsync();
        Assert.Equal("", stored.AccessToken);
        Assert.Equal("", stored.RefreshToken);
    }

    [Fact]
    public async Task GetStatusAsync_AfterRejection_KeepsReturningReconnectRequired_WithoutHttpCall()
    {
        using var db = CreateDb();
        await SeedRejectedTokenAsync(db);
        using var cache = CreateCache();
        var svc = CreateService(db, cache, new FakeHttpClientFactory(new ThrowingHttpMessageHandler()));

        var status = await svc.GetStatusAsync();

        Assert.Equal(GoogleConnectionState.ReconnectRequired, status.State);
        Assert.False(status.Connected);
    }

    [Theory]
    [InlineData("Google answers 503")]
    [InlineData("Google answers invalid_client")]
    [InlineData("network failure")]
    [InlineData("timeout")]
    public async Task GetStatusAsync_RefreshFails_ReturnsUnreachable_AndKeepsToken(string failure)
    {
        using var db = CreateDb();
        await SeedExpiredTokenAsync(db);
        HttpMessageHandler handler = failure switch
        {
            "Google answers 503" => new FakeHttpMessageHandler(System.Net.HttpStatusCode.ServiceUnavailable, ""),
            "Google answers invalid_client" => new FakeHttpMessageHandler(
                System.Net.HttpStatusCode.Unauthorized, """{"error":"invalid_client"}"""),
            "network failure" => new FailingHttpMessageHandler(new HttpRequestException("No such host is known.")),
            _ => new FailingHttpMessageHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")),
        };
        using var cache = CreateCache();
        var svc = CreateService(db, cache, new FakeHttpClientFactory(handler));

        var status = await svc.GetStatusAsync();

        Assert.Equal(GoogleConnectionState.Unreachable, status.State);
        Assert.True(status.Connected);
        using var check = CreateDb();
        var stored = await check.GoogleOAuthTokens.SingleAsync();
        Assert.Equal("old-token", stored.AccessToken);
        Assert.Equal("good-refresh", stored.RefreshToken);
    }

    [Fact]
    public async Task DisconnectAsync_RemovesAllTokens()
    {
        using var db = CreateDb();
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "tok",
            RefreshToken = "ref",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ConnectedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        await svc.DisconnectAsync();

        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_NoToken_ThrowsNotConnected()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache, new FakeHttpClientFactory(new ThrowingHttpMessageHandler()));

        var ex = await Assert.ThrowsAsync<GoogleConnectionException>(() => svc.GetValidAccessTokenAsync());

        Assert.Equal(GoogleConnectionState.NotConnected, ex.State);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshInvalidGrant_ThrowsReconnectRequired_AndBlanksTokens()
    {
        using var db = CreateDb();
        await SeedExpiredTokenAsync(db);
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.BadRequest, InvalidGrant));
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        var ex = await Assert.ThrowsAsync<GoogleConnectionException>(() => svc.GetValidAccessTokenAsync());

        Assert.Equal(GoogleConnectionState.ReconnectRequired, ex.State);
        using var check = CreateDb();
        var stored = await check.GoogleOAuthTokens.SingleAsync();
        Assert.Equal("", stored.AccessToken);
        Assert.Equal("", stored.RefreshToken);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_RejectedToken_ThrowsReconnectRequired_WithoutHttpCall()
    {
        using var db = CreateDb();
        await SeedRejectedTokenAsync(db);
        using var cache = CreateCache();
        var svc = CreateService(db, cache, new FakeHttpClientFactory(new ThrowingHttpMessageHandler()));

        var ex = await Assert.ThrowsAsync<GoogleConnectionException>(() => svc.GetValidAccessTokenAsync());

        Assert.Equal(GoogleConnectionState.ReconnectRequired, ex.State);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshTransientError_ThrowsUnreachable_KeepsToken()
    {
        using var db = CreateDb();
        await SeedExpiredTokenAsync(db);
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.ServiceUnavailable, ""));
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        var ex = await Assert.ThrowsAsync<GoogleConnectionException>(() => svc.GetValidAccessTokenAsync());

        Assert.Equal(GoogleConnectionState.Unreachable, ex.State);
        using var check = CreateDb();
        Assert.Equal("good-refresh", (await check.GoogleOAuthTokens.SingleAsync()).RefreshToken);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_CallerCancelsDuringRefresh_IsNotReportedAsUnreachable()
    {
        using var db = CreateDb();
        await SeedExpiredTokenAsync(db);
        using var cts = new CancellationTokenSource();
        using var cache = CreateCache();
        var svc = CreateService(db, cache, new FakeHttpClientFactory(new CancellingHttpMessageHandler(cts)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.GetValidAccessTokenAsync(cts.Token));
    }

    /// <summary>The caller gives up while Google is still answering.</summary>
    private sealed class CancellingHttpMessageHandler(CancellationTokenSource caller) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            caller.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The caller's token should have cancelled this request.");
        }
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_StaleToken_RefreshSucceeds_ReturnsNewToken()
    {
        using var db = CreateDb();
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "old-token",
            RefreshToken = "good-refresh",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ConnectedAt = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"new-token","expires_in":3600}"""));
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Equal("new-token", result);
        var stored = await db.GoogleOAuthTokens.FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("new-token", stored.AccessToken);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsToken_WithoutHttpCall()
    {
        using var db = CreateDb();
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "still-valid",
            RefreshToken = "refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ConnectedAt = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new ThrowingHttpMessageHandler());
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        var result = await svc.GetValidAccessTokenAsync();

        Assert.Equal("still-valid", result);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ReconnectWithoutDisconnect_UpdatesTokenAndPreservesRefreshToken()
    {
        using var db = CreateDb();
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
        Assert.Equal("new-access", token.AccessToken);
        Assert.Equal("test-refresh", token.RefreshToken);
        Assert.Equal(1, token.Id);
    }

    [Fact]
    public async Task ExchangeCodeAsync_AfterRejection_ConnectsAgain()
    {
        using var db = CreateDb();
        await SeedRejectedTokenAsync(db);
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);
        await svc.ExchangeCodeAsync("auth-code", state!);

        var status = await svc.GetStatusAsync();
        Assert.Equal(GoogleConnectionState.Connected, status.State);
        Assert.True(status.ConnectedAt > DateTime.UtcNow.AddMinutes(-1));
        using var check = CreateDb();
        var stored = await check.GoogleOAuthTokens.SingleAsync();
        Assert.Equal("test-access", stored.AccessToken);
        Assert.Equal("test-refresh", stored.RefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_AfterRejection_WithoutRefreshToken_Throws()
    {
        using var db = CreateDb();
        await SeedRejectedTokenAsync(db);
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"test-access","expires_in":3600}"""));
        using var cache = CreateCache();
        var svc = CreateService(db, cache, factory);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExchangeCodeAsync("auth-code", state!));
        using var check = CreateDb();
        Assert.Equal("", (await check.GoogleOAuthTokens.SingleAsync()).RefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_GoogleReturnsError_ThrowsHttpRequestException()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var svc = CreateService(db, cache, factory);

        svc.GetAuthorizationUrl();
        cache.TryGetValue("google_oauth_state", out string? state);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.ExchangeCodeAsync("bad-code", state!));

        Assert.Empty(await db.GoogleOAuthTokens.ToListAsync());
    }

    [Fact]
    public async Task ConnectDisconnectReconnect_StoresNewTokenWithIdOne()
    {
        using var db = CreateDb();
        using var cache = CreateCache();
        var svc = CreateService(db, cache);

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
