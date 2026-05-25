using FinanceHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinanceHub.Api.Controllers;

[ApiController]
[Route("api/auth/google")]
public class GoogleAuthController(GoogleOAuthService googleOAuth, IConfiguration config) : ControllerBase
{
    private string FrontendUrl => config["GoogleServices:FrontendUrl"] ?? "";

    [HttpGet("login")]
    public IActionResult Login()
    {
        var url = googleOAuth.GetAuthorizationUrl();
        return Ok(new { url });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return Redirect($"{FrontendUrl}/settings?google=error");

        await googleOAuth.ExchangeCodeAsync(code, ct);
        return Redirect($"{FrontendUrl}/settings?google=connected");
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var status = await googleOAuth.GetStatusAsync(ct);
        return Ok(status);
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await googleOAuth.DisconnectAsync(ct);
        return NoContent();
    }
}
