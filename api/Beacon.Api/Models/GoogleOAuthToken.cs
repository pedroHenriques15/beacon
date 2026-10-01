namespace Beacon.Api.Models;

/// <summary>The connected Google account's tokens: one row at most, with <see cref="SingletonId"/>.</summary>
public class GoogleOAuthToken
{
    /// <summary>The only row's id (check constraint <c>CK_SingleToken</c>).</summary>
    public const int SingletonId = 1;

    public int Id { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Scopes { get; set; } = "";
    public DateTime ConnectedAt { get; set; }
}
