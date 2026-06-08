using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Beacon.Tests.Services;

public class GoogleCalendarServiceTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    private static IConfiguration CreateOAuthConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleServices:ClientId"]     = "test-id",
                ["GoogleServices:ClientSecret"] = "test-secret",
                ["GoogleServices:RedirectUri"]  = "http://localhost/callback",
                ["GoogleServices:FrontendUrl"]  = "http://localhost:4200",
            })
            .Build();

    private static GoogleOAuthService CreateOAuthSvcWithToken(AppDbContext db)
    {
        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id           = 1,
            AccessToken  = "test-token",
            RefreshToken = "test-refresh",
            ExpiresAt    = DateTime.UtcNow.AddHours(1),
            ConnectedAt  = DateTime.UtcNow.AddDays(-1),
        });
        db.SaveChanges();

        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        return new GoogleOAuthService(
            new FakeHttpClientFactory(new ThrowingHttpMessageHandler()),
            CreateOAuthConfig(), db, cache);
    }

    private static GoogleOAuthService CreateOAuthSvcNoToken(AppDbContext db)
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        return new GoogleOAuthService(
            new FakeHttpClientFactory(new ThrowingHttpMessageHandler()),
            CreateOAuthConfig(), db, cache);
    }

    private static GoogleCalendarService CreateCalendarSvc(
        GoogleOAuthService oauthSvc, HttpMessageHandler calendarHandler) =>
        new(oauthSvc, new FakeHttpClientFactory(calendarHandler));

    private const string SingleCalendarList = """
        {
          "items": [
            { "id": "primary", "selected": true, "backgroundColor": "#4a86e8" }
          ]
        }
        """;

    [Fact]
    public async Task GetEventsAsync_SinglePage_ReturnsMappedEvents()
    {
        using var db = CreateDb(nameof(GetEventsAsync_SinglePage_ReturnsMappedEvents));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var eventsBody = """
            {
              "items": [
                {
                  "id": "evt1",
                  "summary": "Team meeting",
                  "start": {"dateTime": "2026-05-26T10:00:00Z"},
                  "end":   {"dateTime": "2026-05-26T11:00:00Z"}
                },
                {
                  "id": "evt2",
                  "summary": "Holiday",
                  "start": {"date": "2026-05-25"},
                  "end":   {"date": "2026-05-26"}
                }
              ]
            }
            """;
        var svc = CreateCalendarSvc(oauthSvc,
            new SequentialHttpMessageHandler(
                (System.Net.HttpStatusCode.OK, SingleCalendarList),
                (System.Net.HttpStatusCode.OK, eventsBody)));

        var events = await svc.GetEventsAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

        Assert.Equal(2, events.Count);

        var timed = events[0];
        Assert.Equal("evt1", timed.Id);
        Assert.Equal("Team meeting", timed.Title);
        Assert.False(timed.IsAllDay);
        Assert.Equal("primary", timed.CalendarId);

        var allDay = events[1];
        Assert.Equal("evt2", allDay.Id);
        Assert.Equal("Holiday", allDay.Title);
        Assert.True(allDay.IsAllDay);
        Assert.Equal("2026-05-25", allDay.Start);
        Assert.Equal("2026-05-25", allDay.End);
    }

    [Fact]
    public async Task GetEventsAsync_Pagination_FollowsNextPageToken()
    {
        using var db = CreateDb(nameof(GetEventsAsync_Pagination_FollowsNextPageToken));
        var oauthSvc = CreateOAuthSvcWithToken(db);

        var page1 = """
            {
              "items": [{"id":"p1","summary":"First","start":{"dateTime":"2026-05-26T10:00:00Z"},"end":{"dateTime":"2026-05-26T11:00:00Z"}}],
              "nextPageToken": "tok123"
            }
            """;
        var page2 = """
            {
              "items": [{"id":"p2","summary":"Second","start":{"dateTime":"2026-05-27T10:00:00Z"},"end":{"dateTime":"2026-05-27T11:00:00Z"}}]
            }
            """;
        var svc = CreateCalendarSvc(oauthSvc,
            new SequentialHttpMessageHandler(
                (System.Net.HttpStatusCode.OK, SingleCalendarList),
                (System.Net.HttpStatusCode.OK, page1),
                (System.Net.HttpStatusCode.OK, page2)));

        var events = await svc.GetEventsAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

        Assert.Equal(2, events.Count);
        Assert.Equal("p1", events[0].Id);
        Assert.Equal("p2", events[1].Id);
    }

    [Fact]
    public async Task GetEventsAsync_GoogleApiError_ThrowsWithBody()
    {
        using var db = CreateDb(nameof(GetEventsAsync_GoogleApiError_ThrowsWithBody));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var svc = CreateCalendarSvc(oauthSvc,
            new FakeHttpMessageHandler(System.Net.HttpStatusCode.Unauthorized,
                """{"error":{"message":"Invalid Credentials"}}"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GetEventsAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31)));

        Assert.Contains("401", ex.Message);
        Assert.Contains("Invalid Credentials", ex.Message);
    }

    [Fact]
    public async Task GetEventsAsync_NoOAuthToken_Throws()
    {
        using var db = CreateDb(nameof(GetEventsAsync_NoOAuthToken_Throws));
        var oauthSvc = CreateOAuthSvcNoToken(db);
        var svc = CreateCalendarSvc(oauthSvc, new ThrowingHttpMessageHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GetEventsAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31)));
    }

    [Fact]
    public async Task CreateEventAsync_ReturnsCreatedEvent()
    {
        using var db = CreateDb(nameof(CreateEventAsync_ReturnsCreatedEvent));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var responseBody = """
            {"id":"new1","summary":"New Event","start":{"dateTime":"2026-05-26T14:00:00Z"},"end":{"dateTime":"2026-05-26T15:00:00Z"}}
            """;
        var svc = CreateCalendarSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, responseBody));

        var request = new CreateCalendarEventRequest("New Event", "2026-05-26T14:00:00Z", "2026-05-26T15:00:00Z", null, null, false, null);
        var result = await svc.CreateEventAsync(request);

        Assert.Equal("new1", result.Id);
        Assert.Equal("New Event", result.Title);
        Assert.False(result.IsAllDay);
    }

    [Fact]
    public async Task CreateEventAsync_GoogleApiError_ThrowsWithBody()
    {
        using var db = CreateDb(nameof(CreateEventAsync_GoogleApiError_ThrowsWithBody));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var svc = CreateCalendarSvc(oauthSvc,
            new FakeHttpMessageHandler(System.Net.HttpStatusCode.BadRequest,
                """{"error":{"message":"Required field missing"}}"""));

        var request = new CreateCalendarEventRequest("", "2026-05-26T14:00:00Z", null, null, null, false, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateEventAsync(request));

        Assert.Contains("400", ex.Message);
        Assert.Contains("Required field missing", ex.Message);
    }

    [Fact]
    public async Task UpdateEventAsync_ReturnsUpdatedEvent()
    {
        using var db = CreateDb(nameof(UpdateEventAsync_ReturnsUpdatedEvent));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var responseBody = """
            {"id":"evt1","summary":"Updated","start":{"dateTime":"2026-05-26T14:00:00Z"},"end":{"dateTime":"2026-05-26T15:00:00Z"}}
            """;
        var svc = CreateCalendarSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, responseBody));

        var request = new UpdateCalendarEventRequest("Updated", "2026-05-26T14:00:00Z", "2026-05-26T15:00:00Z", null, null, false, null, null);
        var result = await svc.UpdateEventAsync("evt1", "primary", request);

        Assert.Equal("evt1", result.Id);
        Assert.Equal("Updated", result.Title);
    }

    [Fact]
    public async Task DeleteEventAsync_Succeeds_OnNoContent()
    {
        using var db = CreateDb(nameof(DeleteEventAsync_Succeeds_OnNoContent));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var svc = CreateCalendarSvc(oauthSvc,
            new FakeHttpMessageHandler(System.Net.HttpStatusCode.NoContent, ""));

        await svc.DeleteEventAsync("evt1", "primary");
    }

    [Fact]
    public async Task DeleteEventAsync_GoogleApiError_ThrowsWithBody()
    {
        using var db = CreateDb(nameof(DeleteEventAsync_GoogleApiError_ThrowsWithBody));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var svc = CreateCalendarSvc(oauthSvc,
            new FakeHttpMessageHandler(System.Net.HttpStatusCode.NotFound,
                """{"error":{"message":"Event not found"}}"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteEventAsync("missing-id", "primary"));

        Assert.Contains("404", ex.Message);
        Assert.Contains("Event not found", ex.Message);
    }

    private sealed class SequentialHttpMessageHandler(
        params (System.Net.HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = responses[Math.Min(_index++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

}
