using Beacon.Api.Features.Health.Queries.GetHealth;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(GetHealthQueryHandler getHealth) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var health = await getHealth.HandleAsync(ct);
        return health.Status == GetHealthResponse.Ok
            ? Ok(health)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, health);
    }
}
