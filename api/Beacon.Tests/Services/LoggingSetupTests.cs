using Beacon.Api.Features.Logs.Queries.GetLogs;
using Beacon.Api.Services.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beacon.Tests.Services;

/// <summary>Where log files go, what a missing or unwritable folder does, and which levels reach the logs.</summary>
public class LoggingSetupTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void NoPath_LogsToTheConsoleOnly_WithAWarning()
    {
        var (files, warning) = LogFiles.Resolve(TestPricing.Config(), _temp.Path);

        Assert.Null(files.Directory);
        Assert.Contains("Logs:Path is not set", warning);
    }

    [Fact]
    public void UnwritablePath_LogsToTheConsoleOnly_WithAWarning()
    {
        // A file where the folder should be: the folder can't be created.
        var blocked = Path.Combine(_temp.Path, "taken");
        File.WriteAllText(blocked, "");

        var (files, warning) = LogFiles.Resolve(TestPricing.Config(("Logs:Path", blocked)), _temp.Path);

        Assert.Null(files.Directory);
        Assert.Contains("can't be written", warning);
    }

    [Fact]
    public void RelativePath_IsCreatedUnderTheContentRoot()
    {
        var (files, warning) = LogFiles.Resolve(
            TestPricing.Config(("Logs:Path", "logs"), ("Logs:Keep", "3")), _temp.Path);

        Assert.Null(warning);
        Assert.Equal(Path.Combine(_temp.Path, "logs"), files.Directory);
        Assert.True(Directory.Exists(files.Directory));
        Assert.Equal(3, files.KeepDays);
        Assert.Empty(Directory.GetFiles(files.Directory!)); // the write test leaves nothing behind
    }

    [Fact]
    public async Task Events_LandAsJsonLines_ThatTheLogReaderReads()
    {
        var files = new LogFiles(_temp.Path, LogFiles.DefaultKeepDays);
        using (var factory = LoggerFactory.Create(files.AddTo))
        {
            factory.CreateLogger("Beacon.Api.Services.Sample")
                .LogWarning(new InvalidOperationException("boom"), "Price sync failed for {Name}", "World ETF");
        }

        var file = Assert.Single(Directory.GetFiles(_temp.Path, LogFiles.Pattern));
        Assert.Matches(@"beacon-\d{8}\.json$", file);
        var entry = GetLogsQueryHandler.Parse(Assert.Single(File.ReadAllLines(file)));
        Assert.NotNull(entry);
        Assert.Equal("Warning", entry.Level);
        Assert.Equal("Price sync failed for World ETF", entry.Message);
        Assert.Equal("Beacon.Api.Services.Sample", entry.Source);
        Assert.Contains("boom", entry.Details);

        var page = await new GetLogsQueryHandler(files).HandleAsync(new GetLogsQuery());
        Assert.Equal(entry.Message, Assert.Single(page.Entries).Message);
    }

    [Fact]
    public void FilesOlderThanKeepDays_AreRemoved()
    {
        var old = Path.Combine(_temp.Path, "beacon-20200101.json");
        var recent = Path.Combine(_temp.Path, $"beacon-{DateTime.Now.AddDays(-1):yyyyMMdd}.json");
        File.WriteAllText(old, "");
        File.WriteAllText(recent, "");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-1));

        // The file sink applies the retention when it opens today's file.
        var files = new LogFiles(_temp.Path, keepDays: 14);
        using (var factory = LoggerFactory.Create(files.AddTo))
            factory.CreateLogger("Test").LogInformation("Started");

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", false)] // SQL commands
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", false)]       // per-request framework lines
    [InlineData("System.Net.Http.HttpClient.yahoo-finance.LogicalHandler", false)]
    [InlineData("Microsoft.Hosting.Lifetime", true)]                      // "Now listening on..."
    [InlineData("Beacon.Api.Middleware.RequestLoggingMiddleware", true)]
    public void WithoutLevelSettings_FrameworkInformationIsLeftOut(string category, bool logged)
    {
        var captured = Capture(TestPricing.Config(), category, LogLevel.Information);

        Assert.Equal(logged, captured);
    }

    [Fact]
    public void WithoutLevelSettings_FrameworkWarningsStillGetThrough() =>
        Assert.True(Capture(TestPricing.Config(), "Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning));

    [Fact]
    public async Task TheFiles_FollowTheSameLevelFilters()
    {
        var files = new LogFiles(_temp.Path, LogFiles.DefaultKeepDays);
        var config = TestPricing.Config();
        using (var services = new ServiceCollection()
                   .AddLogging(b =>
                   {
                       b.AddConfiguration(config.GetSection("Logging"));
                       LogLevels.ApplyDefaults(b, config);
                       files.AddTo(b);
                   })
                   .BuildServiceProvider())
        {
            var factory = services.GetRequiredService<ILoggerFactory>();
            factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogInformation("SELECT 1");
            factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").LogInformation("Request starting");
            factory.CreateLogger("Beacon.Api.Middleware.RequestLoggingMiddleware").LogInformation("HTTP GET /api/health");
        }

        var page = await new GetLogsQueryHandler(files).HandleAsync(new GetLogsQuery());
        Assert.Equal(["HTTP GET /api/health"], page.Entries.Select(e => e.Message));
    }

    [Fact]
    public void LevelSettings_OverrideTheDefaults()
    {
        var config = TestPricing.Config(
            ("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Information"),
            ("Logging:LogLevel:Default", "Warning"));

        Assert.True(Capture(config, "Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information));
        Assert.False(Capture(config, "Beacon.Api.Services.Sample", LogLevel.Information));
    }

    /// <summary>Whether an event of <paramref name="level"/> in <paramref name="category"/> gets past the filters.</summary>
    private static bool Capture(Microsoft.Extensions.Configuration.IConfiguration config, string category, LogLevel level)
    {
        var provider = new CapturingLoggerProvider();
        using var services = new ServiceCollection()
            .AddLogging(b =>
            {
                // As WebApplication.CreateBuilder does, then the defaults.
                b.AddConfiguration(config.GetSection("Logging"));
                LogLevels.ApplyDefaults(b, config);
                b.AddProvider(provider);
            })
            .BuildServiceProvider();

        services.GetRequiredService<ILoggerFactory>().CreateLogger(category).Log(level, "Sample");
        return provider.Entries.Count == 1;
    }
}
