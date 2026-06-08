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
    IMemoryCache cache)
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

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync(ct);
        if (token is null) return null;

        if (token.ExpiresAt > DateTime.UtcNow.AddSeconds(ExpiryBufferSeconds))
            return token.AccessToken;

        return await RefreshAccessTokenAsync(token, ct);
    }

    public async Task<GoogleAuthStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var token = await db.GoogleOAuthTokens.FirstOrDefaultAsync(ct);
        if (token is null)
            return new GoogleAuthStatus(false, null, null);

        return new GoogleAuthStatus(true, token.ExpiresAt, token.ConnectedAt);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var tokens = await db.GoogleOAuthTokens.ToListAsync(ct);
        db.GoogleOAuthTokens.RemoveRange(tokens);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> RefreshAccessTokenAsync(Models.GoogleOAuthToken token, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("google-oauth");
            var response = await client.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["refresh_token"] = token.RefreshToken,
                    ["client_id"] = ClientId,
                    ["client_secret"] = ClientSecret,
                    ["grant_type"] = "refresh_token",
                }), ct);

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty refresh response from Google.");

            token.AccessToken = body.AccessToken;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(body.ExpiresIn);
            await db.SaveChangesAsync(ct);

            return token.AccessToken;
        }
        catch (HttpRequestException)
        {
            await DisconnectAsync(ct);
            return null;
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
}

public record GoogleAuthStatus(bool Connected, DateTime? ExpiresAt, DateTime? ConnectedAt);
