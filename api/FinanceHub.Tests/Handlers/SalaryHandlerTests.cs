using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Salary.Commands.CreateSalaryItemCategory;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceHub.Tests.Handlers;

public class SalaryHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateItemCategory_ReturnsError_WhenProfileDoesNotExist()
    {
        await using var db = CreateDb(nameof(CreateItemCategory_ReturnsError_WhenProfileDoesNotExist));
        var handler = new CreateSalaryItemCategoryCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateSalaryItemCategoryCommand(999, "Test", "#ff0000", "deduction"));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateItemCategory_ReturnsCategory_WhenProfileExists()
    {
        await using var db = CreateDb(nameof(CreateItemCategory_ReturnsCategory_WhenProfileExists));
        var profile = new SalaryProfile { Name = "Test Profile" };
        db.SalaryProfiles.Add(profile);
        await db.SaveChangesAsync();

        var handler = new CreateSalaryItemCategoryCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateSalaryItemCategoryCommand(profile.Id, "Bonus", "#00ff00", "earning"));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("Bonus", result.Name);
        Assert.Equal(profile.Id, result.ProfileId);
    }
}
