using System.Net;
using System.Text.Json;
using Beacon.Api.Controllers;
using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Tests.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beacon.Tests.Controllers;

/// <summary>
/// Every Calendar and Tasks endpoint answers a Google connection it cannot use the same way:
/// 401 when the account must be connected (again), 503 when Google could not be reached.
/// </summary>
public class GoogleConnectionErrorsTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private AppDbContext CreateDb() => _database.CreateContext();

    private static (CalendarController Calendar, TasksController Tasks) CreateControllers(
        AppDbContext db, HttpMessageHandler tokenEndpoint, HttpMessageHandler googleApis)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleServices:ClientId"] = "test-id",
                ["GoogleServices:ClientSecret"] = "test-secret",
                ["GoogleServices:RedirectUri"] = "http://localhost/callback",
            })
            .Build();
        var oauth = new GoogleOAuthService(new FakeHttpClientFactory(tokenEndpoint), config, db,
            new MemoryCache(Options.Create(new MemoryCacheOptions())), NullLogger<GoogleOAuthService>.Instance);
        var apis = new FakeHttpClientFactory(googleApis);
        return (new CalendarController(new GoogleCalendarService(oauth, apis)),
            new TasksController(new GoogleTasksService(oauth, apis)));
    }

    private static void SeedToken(AppDbContext db, DateTime expiresAt)
    {
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "old-token",
            RefreshToken = "good-refresh",
            ExpiresAt = expiresAt,
            ConnectedAt = DateTime.UtcNow.AddDays(-30),
        });
        db.SaveChanges();
    }

    private static (string Endpoint, Func<Task<IActionResult>> Call)[] Endpoints(
        CalendarController calendar, TasksController tasks)
    {
        var ct = CancellationToken.None;
        var createEvent = new CreateCalendarEventRequest(
            "Dentist", "2026-05-26T10:00:00Z", "2026-05-26T11:00:00Z", null, null, false, null);
        var updateEvent = new UpdateCalendarEventRequest(
            "Dentist", "2026-05-26T10:00:00Z", "2026-05-26T11:00:00Z", null, null, false, null, "primary");
        return
        [
            ("GET events", () => calendar.GetEvents(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), ct)),
            ("POST event", () => calendar.CreateEvent(createEvent, ct)),
            ("PUT event", () => calendar.UpdateEvent("evt1", updateEvent, ct)),
            ("DELETE event", () => calendar.DeleteEvent("evt1", "primary", ct)),
            ("GET task lists", () => tasks.GetTaskLists(ct)),
            ("GET tasks", () => tasks.GetTasks("list1", ct)),
            ("POST task", () => tasks.CreateTask(new CreateTaskRequest("Buy milk", null, null, "list1"), ct)),
            ("PUT task", () => tasks.UpdateTask("t1", new UpdateTaskRequest("Buy milk", null, null, true, "list1"), ct)),
            ("POST task move", () => tasks.MoveTask("t1", new MoveTaskRequest("list1", "list2", null), ct)),
            ("DELETE task", () => tasks.DeleteTask("t1", "list1", ct)),
        ];
    }

    /// <summary>Compares "endpoint: status code" strings, so a failure names the endpoint.</summary>
    private static async Task AssertAnswersAsync(string endpoint, Func<Task<IActionResult>> call, int status, string code)
    {
        var result = Assert.IsType<ObjectResult>(await call());
        var body = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal($"{endpoint}: {status} {code}", $"{endpoint}: {result.StatusCode} {body.GetProperty("code").GetString()}");
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task EveryEndpoint_RefreshTokenRejected_Answers401ReconnectRequired()
    {
        using var db = CreateDb();
        SeedToken(db, DateTime.UtcNow.AddMinutes(-5));
        var (calendar, tasks) = CreateControllers(db,
            new FakeHttpMessageHandler(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""),
            new ThrowingHttpMessageHandler());

        foreach (var (endpoint, call) in Endpoints(calendar, tasks))
            await AssertAnswersAsync(endpoint, call, 401, "google_reconnect_required");
    }

    [Fact]
    public async Task EveryEndpoint_GoogleUnreachable_Answers503Unreachable_AndKeepsToken()
    {
        using var db = CreateDb();
        SeedToken(db, DateTime.UtcNow.AddMinutes(-5));
        var (calendar, tasks) = CreateControllers(db,
            new FailingHttpMessageHandler(new HttpRequestException("No such host is known.")),
            new ThrowingHttpMessageHandler());

        foreach (var (endpoint, call) in Endpoints(calendar, tasks))
            await AssertAnswersAsync(endpoint, call, 503, "google_unreachable");

        using var check = CreateDb();
        Assert.Equal("good-refresh", (await check.GoogleOAuthTokens.SingleAsync()).RefreshToken);
    }

    [Fact]
    public async Task EveryEndpoint_NoAccount_Answers401NotConnected()
    {
        using var db = CreateDb();
        var (calendar, tasks) = CreateControllers(db, new ThrowingHttpMessageHandler(), new ThrowingHttpMessageHandler());

        foreach (var (endpoint, call) in Endpoints(calendar, tasks))
            await AssertAnswersAsync(endpoint, call, 401, "google_not_connected");
    }

    [Fact]
    public async Task GoogleApiError_Answers502WithGooglesMessage()
    {
        using var db = CreateDb();
        SeedToken(db, DateTime.UtcNow.AddHours(1));
        var (calendar, _) = CreateControllers(db, new ThrowingHttpMessageHandler(),
            new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, """{"error":{"message":"Backend Error"}}"""));

        var result = Assert.IsType<ObjectResult>(
            await calendar.GetEvents(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), CancellationToken.None));

        Assert.Equal(502, result.StatusCode);
        Assert.Contains("Backend Error", JsonSerializer.SerializeToElement(result.Value).GetProperty("error").GetString());
    }
}
