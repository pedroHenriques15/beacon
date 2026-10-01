using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Beacon.Api.Services;

public class GoogleOAuthService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    AppDbContext db,
    IMemoryCache cache,
    ILogger<GoogleOAuthService> logger)
{
    private const string StateCacheKey = "google_oauth_state";
    private const int ExpiryBufferSeconds = 60;

    private static readonly string[] DefaultScopes =
    [
        "https://www.googleapis.com/auth/calendar",
        "https://www.googleapis.com/auth/tasks",
    ];

    private string ClientId => config["GoogleServices:ClientId"]
        ?? throw new InvalidOperationException("GoogleServices:ClientId is not configured.");

    private string ClientSecret => config["GoogleServices:ClientSecret"]
        ?? throw new InvalidOperationException("GoogleServices:ClientSecret is not configured.");

    private string RedirectUri => config["GoogleServices:RedirectUri"]
        ?? throw new InvalidOperationException("GoogleServices:RedirectUri is not configured.");

    public string GetAuthorizationUrl()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        cache.Set(StateCacheKey, state, TimeSpan.FromMinutes(10));

        var scopes = string.Join(" ", DefaultScopes);
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = ClientId;
        query["redirect_uri"] = RedirectUri;
        query["response_type"] = "code";
        query["scope"] = scopes;
        query["access_type"] = "offline";
        query["prompt"] = "consent";
        query["state"] = state;
        return $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
    }

    public async Task ExchangeCodeAsync(string code, string state, CancellationToken ct = default)
    {
        if (!cache.TryGetValue(StateCacheKey, out string? expectedState) || expectedState != state)
            throw new InvalidOperationException("Invalid or expired OAuth state parameter.");

        cache.Remove(StateCacheKey);

        var client = httpClientFactory.CreateClient("google-oauth");
        var response = await client.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["redirect_uri"] = RedirectUri,
                ["grant_type"] = "authorization_code",
            }), ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty token response from Google.");

        await UpsertTokenAsync(body, ct);
    }

    /// <summary>
    /// An access token that works, refreshed first when it has expired. Throws
    /// <see cref="GoogleConnectionException"/> when there is none: no account is connected,
    /// Google rejected the refresh token, or Google could not be reached.
    /// </summary>
    public async Task<string> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync(ct)
            ?? throw new GoogleConnectionException(GoogleConnectionState.NotConnected);

        var state = await RefreshIfExpiredAsync(token, ct);
        return state == GoogleConnectionState.Connected
            ? token.AccessToken
            : throw new GoogleConnectionException(state);
    }

    /// <summary>
    /// Whether the stored token still works. An expired access token is refreshed first, so a
    /// refresh token Google no longer accepts shows here, not only as an empty calendar.
    /// </summary>
    public async Task<GoogleAuthStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync(ct);
        if (token is null)
            return new GoogleAuthStatus(GoogleConnectionState.NotConnected, null, null);

        var state = await RefreshIfExpiredAsync(token, ct);
        return new GoogleAuthStatus(state, token.ExpiresAt, token.ConnectedAt);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var tokens = await db.GoogleOAuthTokens.ToListAsync(ct);
        db.GoogleOAuthTokens.RemoveRange(tokens);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Refreshes an expired access token and returns the connection's state. When Google rejects
    /// the refresh token (<c>invalid_grant</c>: it expired or was revoked), the row stays with
    /// both tokens blanked, so every later check reports ReconnectRequired until the account is
    /// connected again or disconnected. Any other failure keeps the token for the next attempt.
    /// </summary>
    private async Task<string> RefreshIfExpiredAsync(Models.GoogleOAuthToken token, CancellationToken ct)
    {
        if (token.RefreshToken.Length == 0)
            return GoogleConnectionState.ReconnectRequired;

        if (token.ExpiresAt > DateTime.UtcNow.AddSeconds(ExpiryBufferSeconds))
            return GoogleConnectionState.Connected;

        try
        {
            var client = httpClientFactory.CreateClient("google-oauth");
            using var response = await client.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["refresh_token"] = token.RefreshToken,
                    ["client_id"] = ClientId,
                    ["client_secret"] = ClientSecret,
                    ["grant_type"] = "refresh_token",
                }), ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<TokenErrorResponse>(cancellationToken: ct);
                if (error?.Error != "invalid_grant")
                {
                    logger.LogWarning("Refreshing the Google access token failed with {Status} ({Error}); keeping the token.",
                        (int)response.StatusCode, error?.Error);
                    return GoogleConnectionState.Unreachable;
                }

                logger.LogWarning("Google rejected the stored refresh token; the account must be connected again.");
                token.AccessToken = "";
                token.RefreshToken = "";
                await db.SaveChangesAsync(ct);
                return GoogleConnectionState.ReconnectRequired;
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new JsonException("Empty refresh response from Google.");

            token.AccessToken = body.AccessToken;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(body.ExpiresIn);
            await db.SaveChangesAsync(ct);
            return GoogleConnectionState.Connected;
        }
        // No answer (network, DNS, the client's timeout) or one that is not the JSON Google sends.
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Refreshing the Google access token failed; keeping the token.");
            return GoogleConnectionState.Unreachable;
        }
    }

    private async Task UpsertTokenAsync(TokenResponse body, CancellationToken ct)
    {
        var existing = await db.GoogleOAuthTokens.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            if (string.IsNullOrEmpty(body.RefreshToken))
                throw new InvalidOperationException("Google did not return a refresh token.");

            db.GoogleOAuthTokens.Add(new Models.GoogleOAuthToken
            {
                Id = 1,
                AccessToken = body.AccessToken,
                RefreshToken = body.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(body.ExpiresIn),
                Scopes = string.Join(" ", DefaultScopes),
                ConnectedAt = DateTime.UtcNow,
            });
        }
        else
        {
            // A blank refresh token is one Google rejected: this is a new connection, and it
            // needs a new refresh token.
            if (existing.RefreshToken.Length == 0)
            {
                if (string.IsNullOrEmpty(body.RefreshToken))
                    throw new InvalidOperationException("Google did not return a refresh token.");
                existing.ConnectedAt = DateTime.UtcNow;
            }

            existing.AccessToken = body.AccessToken;
            if (!string.IsNullOrEmpty(body.RefreshToken))
                existing.RefreshToken = body.RefreshToken;
            existing.ExpiresAt = DateTime.UtcNow.AddSeconds(body.ExpiresIn);
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class TokenErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}

/// <summary>The states of the Google connection that <see cref="GoogleAuthStatus"/> reports.</summary>
public static class GoogleConnectionState
{
    public const string NotConnected = "notConnected";
    public const string Connected = "connected";

    /// <summary>Google rejected the refresh token (it expired or was revoked): connect again.</summary>
    public const string ReconnectRequired = "reconnectRequired";

    /// <summary>The access token expired and refreshing it failed for another reason; the token is kept.</summary>
    public const string Unreachable = "unreachable";
}

public record GoogleAuthStatus(string State, DateTime? ExpiresAt, DateTime? ConnectedAt)
{
    /// <summary>The stored token works, or will once Google answers again.</summary>
    public bool Connected => State is GoogleConnectionState.Connected or GoogleConnectionState.Unreachable;
}
