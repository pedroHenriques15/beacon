using Beacon.Api.Controllers;
using Beacon.Api.Data;
using Beacon.Api.Features.Health.Queries.GetHealth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beacon.Tests.Controllers;

/// <summary>
/// Deploy scripts and monitors read the status code: 200 when healthy, 503 when degraded.
/// </summary>
public class HealthControllerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static HealthController CreateController(AppDbContext db) =>
        new(new GetHealthQueryHandler(db, NullLogger<GetHealthQueryHandler>.Instance));

    [Fact]
    public async Task Healthy_Returns200()
    {
        await using var db = _database.CreateContext();

        var result = await CreateController(db).Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(GetHealthResponse.Ok, Assert.IsType<GetHealthResponse>(ok.Value).Status);
    }

    [Fact]
    public async Task Degraded_Returns503WithTheReason()
    {
        await using var db = _database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\"");

        var result = await CreateController(db).Get(CancellationToken.None);

        var degraded = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, degraded.StatusCode);
        Assert.Equal("migrations pending", Assert.IsType<GetHealthResponse>(degraded.Value).Reason);
    }
}
