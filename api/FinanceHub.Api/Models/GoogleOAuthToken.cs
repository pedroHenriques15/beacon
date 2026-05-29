namespace FinanceHub.Api.Models;

public class GoogleOAuthToken
{
    public int Id { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Scopes { get; set; } = "";
    public DateTime ConnectedAt { get; set; }
}
