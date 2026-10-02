using System.Net;
using System.Net.Http.Json;
using Beacon.Api.Controllers;
using Beacon.Api.Features.Logs.Commands.LogClientError;
using Beacon.Api.Features.Logs.Queries.GetLogs;
using Beacon.Api.Middleware;
using Beacon.Api.Services.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beacon.Tests.Controllers;

/// <summary>
/// The client-error route through the real pipeline pieces, on Kestrel (the test host enforces no
/// request size limit): its size cap and its rate limit.
/// </summary>
public class LogsControllerTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(LogsController).Assembly);
        builder.Services.AddSingleton(new LogFiles(null, LogFiles.DefaultKeepDays));
        builder.Services.AddScoped<GetLogsQueryHandler>();
        builder.Services.AddScoped<LogClientErrorCommandHandler>();
        builder.Services.AddClientErrorRateLimit();

        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.UseRateLimiter();
        _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> PostAsync(string message) =>
        _client.PostAsJsonAsync("/api/logs/client-errors", new { message, stack = (string?)null, route = "/settings" });

    [Fact]
    public async Task ClientErrors_AreAccepted_UpToTheRateLimit_ThenRefused()
    {
        for (var i = 0; i < ClientErrorRateLimit.PermitsPerMinute; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await PostAsync($"Error {i}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync("One too many")).StatusCode);
    }

    [Fact]
    public async Task ClientError_OverTheSizeCap_IsRefused()
    {
        var response = await PostAsync(new string('x', LogsController.ClientErrorMaxBytes));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Logs_WithoutFileLogging_SayTheyAreOff()
    {
        var page = await _client.GetFromJsonAsync<GetLogsResponse>("/api/logs");

        Assert.False(page!.Enabled);
    }
}
