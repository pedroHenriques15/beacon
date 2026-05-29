using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using FinanceHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FinanceHub.Tests.Services;

public class GoogleTasksServiceTests
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

    private static GoogleTasksService CreateTasksSvc(
        GoogleOAuthService oauthSvc, HttpMessageHandler handler) =>
        new(oauthSvc, new FakeHttpClientFactory(handler));

    [Fact]
    public async Task GetTaskListsAsync_ReturnsMappedLists()
    {
        using var db = CreateDb(nameof(GetTaskListsAsync_ReturnsMappedLists));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var body = """
            {
              "items": [
                { "id": "list1", "title": "My Tasks" },
                { "id": "list2", "title": "Work" }
              ]
            }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body));

        var lists = await svc.GetTaskListsAsync();

        Assert.Equal(2, lists.Count);
        Assert.Equal("list1", lists[0].Id);
        Assert.Equal("My Tasks", lists[0].Title);
        Assert.Equal("list2", lists[1].Id);
        Assert.Equal("Work", lists[1].Title);
    }

    [Fact]
    public async Task GetTasksAsync_SetsTaskListIdFromParam()
    {
        using var db = CreateDb(nameof(GetTasksAsync_SetsTaskListIdFromParam));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var body = """
            {
              "items": [
                { "id": "task1", "title": "Do laundry", "status": "needsAction" }
              ]
            }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body));

        var tasks = await svc.GetTasksAsync("my-list-id");

        Assert.Single(tasks);
        Assert.Equal("my-list-id", tasks[0].TaskListId);
    }

    [Fact]
    public async Task GetTasksAsync_StripsRFC3339ToDueDate()
    {
        using var db = CreateDb(nameof(GetTasksAsync_StripsRFC3339ToDueDate));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var body = """
            {
              "items": [
                {
                  "id": "task1",
                  "title": "Call dentist",
                  "status": "needsAction",
                  "due": "2026-05-26T00:00:00.000Z"
                }
              ]
            }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body));

        var tasks = await svc.GetTasksAsync("list1");

        Assert.Equal("2026-05-26", tasks[0].Due);
    }

    [Fact]
    public async Task GetTasksAsync_MapsCompletedStatus()
    {
        using var db = CreateDb(nameof(GetTasksAsync_MapsCompletedStatus));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var body = """
            {
              "items": [
                { "id": "t1", "title": "Done", "status": "completed" },
                { "id": "t2", "title": "Pending", "status": "needsAction" }
              ]
            }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body));

        var tasks = await svc.GetTasksAsync("list1");

        Assert.True(tasks[0].Completed);
        Assert.False(tasks[1].Completed);
    }

    [Fact]
    public async Task GetTasksAsync_EmptyItemsResponse_ReturnsEmptyList()
    {
        using var db = CreateDb(nameof(GetTasksAsync_EmptyItemsResponse_ReturnsEmptyList));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var body = """{}""";
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body));

        var tasks = await svc.GetTasksAsync("list1");

        Assert.Empty(tasks);
    }

    [Fact]
    public async Task GetTasksAsync_NoOAuthToken_Throws()
    {
        using var db = CreateDb(nameof(GetTasksAsync_NoOAuthToken_Throws));
        var oauthSvc = CreateOAuthSvcNoToken(db);
        var svc = CreateTasksSvc(oauthSvc, new ThrowingHttpMessageHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GetTasksAsync("list1"));
    }

    [Fact]
    public async Task CreateTaskAsync_ReturnsCreatedTaskWithCorrectListId()
    {
        using var db = CreateDb(nameof(CreateTaskAsync_ReturnsCreatedTaskWithCorrectListId));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var responseBody = """
            { "id": "new-task-1", "title": "Buy milk", "status": "needsAction" }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, responseBody));

        var request = new CreateTaskRequest("Buy milk", null, null, "my-list-id");
        var result = await svc.CreateTaskAsync(request);

        Assert.Equal("new-task-1", result.Id);
        Assert.Equal("Buy milk", result.Title);
        Assert.Equal("my-list-id", result.TaskListId);
        Assert.False(result.Completed);
    }

    [Fact]
    public async Task UpdateTaskAsync_MarkingComplete_ReturnsMappedTask()
    {
        using var db = CreateDb(nameof(UpdateTaskAsync_MarkingComplete_ReturnsMappedTask));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var responseBody = """
            {
              "id": "task1",
              "title": "Call dentist",
              "status": "completed",
              "completed": "2026-05-27T10:00:00.000Z"
            }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, responseBody));

        var request = new UpdateTaskRequest("Call dentist", null, null, true, "list1");
        var result = await svc.UpdateTaskAsync("task1", request);

        Assert.True(result.Completed);
        Assert.Equal("2026-05-27", result.CompletedAt?[..10]);
    }

    [Fact]
    public async Task UpdateTaskAsync_MarkingIncomplete_ReturnsMappedTask()
    {
        using var db = CreateDb(nameof(UpdateTaskAsync_MarkingIncomplete_ReturnsMappedTask));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var responseBody = """
            {
              "id": "task1",
              "title": "Call dentist",
              "status": "needsAction"
            }
            """;
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, responseBody));

        var request = new UpdateTaskRequest("Call dentist", null, null, false, "list1");
        var result = await svc.UpdateTaskAsync("task1", request);

        Assert.False(result.Completed);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public async Task DeleteTaskAsync_SuccessfulDelete_ReturnsWithoutException()
    {
        using var db = CreateDb(nameof(DeleteTaskAsync_SuccessfulDelete_ReturnsWithoutException));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.NoContent, ""));

        await svc.DeleteTaskAsync("task1", "list1");
    }

    [Fact]
    public async Task DeleteTaskAsync_ApiError_Throws()
    {
        using var db = CreateDb(nameof(DeleteTaskAsync_ApiError_Throws));
        var oauthSvc = CreateOAuthSvcWithToken(db);
        var body = """{"error": {"message": "Task not found"}}""";
        var svc = CreateTasksSvc(oauthSvc, new FakeHttpMessageHandler(System.Net.HttpStatusCode.NotFound, body));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteTaskAsync("task1", "list1"));
        Assert.Contains("404", ex.Message);
    }
}
