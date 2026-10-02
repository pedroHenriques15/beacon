using Beacon.Api.Features.Logs.Commands.LogClientError;
using Beacon.Api.Features.Logs.Queries.GetLogs;
using Beacon.Api.Services.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(GetLogsQueryHandler getLogs, LogClientErrorCommandHandler logClientError) : ControllerBase
{
    public const int ClientErrorMaxBytes = 16 * 1024;

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? minLevel, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? search, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(await getLogs.HandleAsync(new GetLogsQuery(minLevel, from, to, search, limit), ct));

    [HttpPost("client-errors")]
    [RequestSizeLimit(ClientErrorMaxBytes)]
    [EnableRateLimiting(ClientErrorRateLimit.Policy)]
    public IActionResult LogClientError(LogClientErrorCommand command)
    {
        logClientError.Handle(command);
        return NoContent();
    }
}
