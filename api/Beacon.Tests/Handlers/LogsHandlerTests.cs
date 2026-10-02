using System.Text.Json;
using Beacon.Api.Features.Logs.Commands.LogClientError;
using Beacon.Api.Features.Logs.Queries.GetLogs;
using Beacon.Api.Services.Logging;
using Beacon.Tests.Services;
using Microsoft.Extensions.Logging;

namespace Beacon.Tests.Handlers;

/// <summary>Reading the log files back, and the errors the web client reports.</summary>
public class LogsHandlerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private LogFiles Files() => new(_temp.Path, LogFiles.DefaultKeepDays);

    private static string Line(string time, string? level, string template, string source = "Beacon.Api.Sample",
        string? exception = null, params (string Name, object Value)[] properties)
    {
        var fields = new Dictionary<string, object> { ["@t"] = time, ["@mt"] = template, ["SourceContext"] = source };
        if (level is not null) fields["@l"] = level;
        if (exception is not null) fields["@x"] = exception;
        foreach (var (name, value) in properties) fields[name] = value;
        return JsonSerializer.Serialize(fields);
    }

    /// <summary>Two days of logs: the 29th's file, then the 30th's, each oldest line first.</summary>
    private void SeedTwoDays()
    {
        File.WriteAllLines(Path.Combine(_temp.Path, "beacon-20260929.json"),
        [
            Line("2026-09-29T08:00:00Z", null, "Started"),
            Line("2026-09-29T09:00:00Z", "Error", "Google refresh failed", "Beacon.Api.Services.GoogleOAuthService",
                exception: "System.Net.Http.HttpRequestException: timed out"),
        ]);
        File.WriteAllLines(Path.Combine(_temp.Path, "beacon-20260930.json"),
        [
            Line("2026-09-30T08:00:00Z", null, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0} ms",
                "Beacon.Api.Middleware.RequestLoggingMiddleware",
                properties: [("Method", "GET"), ("Path", "/api/transactions"), ("StatusCode", 200), ("ElapsedMs", 12.6)]),
            Line("2026-09-30T09:00:00Z", "Warning", "Price jump for {Name}", properties: [("Name", "Gold")]),
            "{\"@t\":\"2026-09-30T10:00:00Z\",\"@mt\":\"half a li", // a line cut short while being written
        ]);
    }

    [Fact]
    public async Task Entries_ComeNewestFirst_AcrossFiles_WithTheirMessagesRendered()
    {
        SeedTwoDays();

        var page = await new GetLogsQueryHandler(Files()).HandleAsync(new GetLogsQuery());

        Assert.True(page.Enabled);
        Assert.False(page.More);
        Assert.Equal(
            ["Price jump for Gold", "HTTP GET /api/transactions responded 200 in 13 ms", "Google refresh failed", "Started"],
            page.Entries.Select(e => e.Message));
        Assert.Equal(["Warning", "Information", "Error", "Information"], page.Entries.Select(e => e.Level));
        Assert.Equal("System.Net.Http.HttpRequestException: timed out", page.Entries[2].Details);
    }

    [Fact]
    public async Task MinimumLevel_LeavesOutLowerLevels()
    {
        SeedTwoDays();

        var page = await new GetLogsQueryHandler(Files()).HandleAsync(new GetLogsQuery(MinLevel: "warning"));

        Assert.Equal(["Warning", "Error"], page.Entries.Select(e => e.Level));
    }

    [Fact]
    public async Task TimeWindow_KeepsOnlyEntriesInside()
    {
        SeedTwoDays();

        var page = await new GetLogsQueryHandler(Files()).HandleAsync(new GetLogsQuery(
            From: DateTimeOffset.Parse("2026-09-29T08:30:00Z"), To: DateTimeOffset.Parse("2026-09-30T08:30:00Z")));

        Assert.Equal(["HTTP GET /api/transactions responded 200 in 13 ms", "Google refresh failed"],
            page.Entries.Select(e => e.Message));
    }

    [Theory]
    [InlineData("GOOGLE", "Google refresh failed")]       // the message, ignoring case
    [InlineData("timed out", "Google refresh failed")]    // the exception
    [InlineData("RequestLogging", "HTTP GET /api/transactions responded 200 in 13 ms")] // the source
    public async Task Search_LooksInMessageDetailsAndSource(string search, string expected)
    {
        SeedTwoDays();

        var page = await new GetLogsQueryHandler(Files()).HandleAsync(new GetLogsQuery(Search: search));

        Assert.Equal(expected, Assert.Single(page.Entries).Message);
    }

    [Fact]
    public async Task Limit_CapsTheEntries_AndSaysThereAreMore()
    {
        SeedTwoDays();

        var page = await new GetLogsQueryHandler(Files()).HandleAsync(new GetLogsQuery(Limit: 2));

        Assert.Equal(2, page.Entries.Count);
        Assert.True(page.More);
        Assert.Equal("Price jump for Gold", page.Entries[0].Message);
    }

    [Fact]
    public async Task Limit_NeverExceedsTheMaximum()
    {
        File.WriteAllLines(Path.Combine(_temp.Path, "beacon-20260930.json"),
            Enumerable.Range(0, GetLogsQueryHandler.MaxLimit + 5)
                .Select(i => Line(DateTimeOffset.Parse("2026-09-30T00:00:00Z").AddSeconds(i).ToString("O"), null, "Line")));

        var page = await new GetLogsQueryHandler(Files()).HandleAsync(new GetLogsQuery(Limit: 1_000_000));

        Assert.Equal(GetLogsQueryHandler.MaxLimit, page.Entries.Count);
        Assert.True(page.More);
    }

    [Fact]
    public async Task WithoutFileLogging_TheAnswerSaysSo()
    {
        var page = await new GetLogsQueryHandler(new LogFiles(null, 14)).HandleAsync(new GetLogsQuery());

        Assert.False(page.Enabled);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public async Task ClientError_IsLoggedAtError_WithItsStack_AndReadBack()
    {
        var files = Files();
        using (var factory = LoggerFactory.Create(files.AddTo))
        {
            new LogClientErrorCommandHandler(factory).Handle(new LogClientErrorCommand(
                "Cannot read properties of undefined (reading 'id')",
                "TypeError: Cannot read properties of undefined\n    at InvestmentsComponent.toggleExpand",
                "/investments"));
        }

        var entry = Assert.Single((await new GetLogsQueryHandler(files).HandleAsync(new GetLogsQuery())).Entries);
        Assert.Equal("Error", entry.Level);
        Assert.Equal(LogClientErrorCommandHandler.Category, entry.Source);
        Assert.Equal("Client error on /investments: Cannot read properties of undefined (reading 'id')", entry.Message);
        Assert.Contains("InvestmentsComponent.toggleExpand", entry.Details);
    }

    [Fact]
    public void ClientError_FieldsAreCutToTheirCaps()
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));

        new LogClientErrorCommandHandler(factory).Handle(new LogClientErrorCommand(
            new string('m', 50_000), new string('s', 50_000), new string('r', 5_000)));

        var (category, level, message) = Assert.Single(provider.Entries);
        Assert.Equal(LogClientErrorCommandHandler.Category, category);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains(new string('m', LogClientErrorCommandHandler.MaxMessageLength) + "…", message);
        Assert.DoesNotContain(new string('m', LogClientErrorCommandHandler.MaxMessageLength + 1), message);
        Assert.Contains(new string('r', LogClientErrorCommandHandler.MaxRouteLength) + "…", message);
        Assert.DoesNotContain(new string('r', LogClientErrorCommandHandler.MaxRouteLength + 1), message);
    }

    [Fact]
    public void ClientError_WithNothingInIt_StillLogsALine()
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));

        new LogClientErrorCommandHandler(factory).Handle(new LogClientErrorCommand(null, null, null));

        Assert.Equal("Client error on (unknown route): (no message)", Assert.Single(provider.Entries).Message);
    }
}
