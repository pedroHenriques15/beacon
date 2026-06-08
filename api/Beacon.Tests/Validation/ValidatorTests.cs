using Beacon.Api.Features.Categories.Commands.CreateCategory;
using Beacon.Api.Features.Categories.Commands.CreateCategoryRule;
using Beacon.Api.Features.Categories.Commands.UpdateCategory;
using Beacon.Api.Features.Categories.Commands.UpdateCategoryRule;
using Beacon.Api.Features.Transactions.Commands.CreateTransaction;
using Beacon.Api.Features.Transactions.Commands.MarkTransfers;
using Beacon.Api.Features.Transactions.Commands.UpdateTransaction;
using Beacon.Api.Features.Transactions.Queries.GetTransactions;
using Xunit;

namespace Beacon.Tests.Validation;

public class ValidatorTests
{
    [Fact]
    public void CreateCategoryCommandValidator_ValidInput_IsValid()
    {
        var validator = new CreateCategoryCommandValidator();
        var result    = validator.Validate(new CreateCategoryCommand("Groceries", "#aaa", "LIDL", null));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CreateCategoryCommandValidator_NullName_IsInvalid()
    {
        var validator = new CreateCategoryCommandValidator();
        var result    = validator.Validate(new CreateCategoryCommand(null!, "#aaa", null, null));
        Assert.False(result.IsValid);
        Assert.Contains("Name is required.", result.Errors);
    }

    [Fact]
    public void CreateCategoryCommandValidator_WhitespaceName_IsInvalid()
    {
        var validator = new CreateCategoryCommandValidator();
        var result    = validator.Validate(new CreateCategoryCommand("   ", "#aaa", null, null));
        Assert.False(result.IsValid);
        Assert.Contains("Name is required.", result.Errors);
    }

    [Fact]
    public void CreateCategoryCommandValidator_NegativeValue_IsInvalid()
    {
        var validator = new CreateCategoryCommandValidator();
        var result    = validator.Validate(new CreateCategoryCommand("Test", "#aaa", "PAT", -1m));
        Assert.False(result.IsValid);
        Assert.Contains("Value must be non-negative.", result.Errors);
    }

    [Fact]
    public void CreateCategoryRuleCommandValidator_ValidInput_IsValid()
    {
        var validator = new CreateCategoryRuleCommandValidator();
        var result    = validator.Validate(new CreateCategoryRuleCommand(1, "LIDL", null));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CreateCategoryRuleCommandValidator_InvalidCategoryId_IsInvalid(int categoryId)
    {
        var validator = new CreateCategoryRuleCommandValidator();
        var result    = validator.Validate(new CreateCategoryRuleCommand(categoryId, "PAT", null));
        Assert.False(result.IsValid);
        Assert.Contains("CategoryId must be a positive integer.", result.Errors);
    }

    [Fact]
    public void CreateCategoryRuleCommandValidator_NeitherPatternNorValue_IsInvalid()
    {
        var validator = new CreateCategoryRuleCommandValidator();
        var result    = validator.Validate(new CreateCategoryRuleCommand(1, null, null));
        Assert.False(result.IsValid);
        Assert.Contains("At least one of Pattern or Value is required.", result.Errors);
    }

    [Fact]
    public void CreateCategoryRuleCommandValidator_NegativeValue_IsInvalid()
    {
        var validator = new CreateCategoryRuleCommandValidator();
        var result    = validator.Validate(new CreateCategoryRuleCommand(1, null, -1m));
        Assert.False(result.IsValid);
        Assert.Contains("Value must be non-negative.", result.Errors);
    }

    [Fact]
    public void UpdateCategoryRuleCommandValidator_ValidInput_IsValid()
    {
        var validator = new UpdateCategoryRuleCommandValidator();
        var result    = validator.Validate(new UpdateCategoryRuleCommand(1, "NEW", null));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void UpdateCategoryRuleCommandValidator_InvalidId_IsInvalid(int id)
    {
        var validator = new UpdateCategoryRuleCommandValidator();
        var result    = validator.Validate(new UpdateCategoryRuleCommand(id, "PAT", null));
        Assert.False(result.IsValid);
        Assert.Contains("Id must be a positive integer.", result.Errors);
    }

    [Fact]
    public void UpdateCategoryRuleCommandValidator_NeitherPatternNorValue_IsInvalid()
    {
        var validator = new UpdateCategoryRuleCommandValidator();
        var result    = validator.Validate(new UpdateCategoryRuleCommand(1, null, null));
        Assert.False(result.IsValid);
        Assert.Contains("At least one of Pattern or Value is required.", result.Errors);
    }

    [Fact]
    public void UpdateCategoryRuleCommandValidator_NegativeValue_IsInvalid()
    {
        var validator = new UpdateCategoryRuleCommandValidator();
        var result    = validator.Validate(new UpdateCategoryRuleCommand(1, null, -5m));
        Assert.False(result.IsValid);
        Assert.Contains("Value must be non-negative.", result.Errors);
    }

    [Fact]
    public void UpdateCategoryCommandValidator_ValidInput_IsValid()
    {
        var validator = new UpdateCategoryCommandValidator();
        var result    = validator.Validate(new UpdateCategoryCommand(1, "NewName", null));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateCategoryCommandValidator_InvalidId_IsInvalid(int id)
    {
        var validator = new UpdateCategoryCommandValidator();
        var result    = validator.Validate(new UpdateCategoryCommand(id, "Name", null));
        Assert.False(result.IsValid);
        Assert.Contains("Id must be a positive integer.", result.Errors);
    }

    [Fact]
    public void UpdateCategoryCommandValidator_NeitherNameNorColor_IsInvalid()
    {
        var validator = new UpdateCategoryCommandValidator();
        var result    = validator.Validate(new UpdateCategoryCommand(1, null, null));
        Assert.False(result.IsValid);
        Assert.Contains("At least one of Name or Color must be provided.", result.Errors);
    }

    [Fact]
    public void UpdateCategoryCommandValidator_WhitespaceNameAndNullColor_IsInvalid()
    {
        var validator = new UpdateCategoryCommandValidator();
        var result    = validator.Validate(new UpdateCategoryCommand(1, "   ", null));
        Assert.False(result.IsValid);
        Assert.Contains("At least one of Name or Color must be provided.", result.Errors);
    }

    private static readonly DateOnly AnyDate = new(2024, 1, 1);

    [Fact]
    public void CreateTransactionCommandValidator_ValidInput_IsValid()
    {
        var validator = new CreateTransactionCommandValidator();
        var result    = validator.Validate(new CreateTransactionCommand(1, AnyDate, AnyDate, "Desc", 100m, "debit", 900m, null));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateTransactionCommandValidator_InvalidStatementId_IsInvalid(int statementId)
    {
        var validator = new CreateTransactionCommandValidator();
        var result    = validator.Validate(new CreateTransactionCommand(statementId, AnyDate, AnyDate, "Desc", 100m, "debit", 900m, null));
        Assert.False(result.IsValid);
        Assert.Contains("StatementId must be a positive integer.", result.Errors);
    }

    [Fact]
    public void CreateTransactionCommandValidator_EmptyDescription_IsInvalid()
    {
        var validator = new CreateTransactionCommandValidator();
        var result    = validator.Validate(new CreateTransactionCommand(1, AnyDate, AnyDate, "", 100m, "debit", 900m, null));
        Assert.False(result.IsValid);
        Assert.Contains("Description is required.", result.Errors);
    }

    [Fact]
    public void CreateTransactionCommandValidator_NegativeAmount_IsInvalid()
    {
        var validator = new CreateTransactionCommandValidator();
        var result    = validator.Validate(new CreateTransactionCommand(1, AnyDate, AnyDate, "Desc", -1m, "debit", 900m, null));
        Assert.False(result.IsValid);
        Assert.Contains("Amount must be non-negative.", result.Errors);
    }

    [Theory]
    [InlineData("Credit")]
    [InlineData("DEBIT")]
    [InlineData("transfer")]
    public void CreateTransactionCommandValidator_InvalidType_IsInvalid(string type)
    {
        var validator = new CreateTransactionCommandValidator();
        var result    = validator.Validate(new CreateTransactionCommand(1, AnyDate, AnyDate, "Desc", 100m, type, 900m, null));
        Assert.False(result.IsValid);
        Assert.Contains("Type must be 'credit' or 'debit'.", result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void CreateTransactionCommandValidator_InvalidCategoryId_IsInvalid(int categoryId)
    {
        var validator = new CreateTransactionCommandValidator();
        var result    = validator.Validate(new CreateTransactionCommand(1, AnyDate, AnyDate, "Desc", 100m, "credit", 900m, categoryId));
        Assert.False(result.IsValid);
        Assert.Contains("CategoryId must be a positive integer.", result.Errors);
    }

    [Fact]
    public void UpdateTransactionCommandValidator_ValidInput_IsValid()
    {
        var validator = new UpdateTransactionCommandValidator();
        var result    = validator.Validate(new UpdateTransactionCommand(1, null, null, "New desc", null, null, null, null, null, false, false));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateTransactionCommandValidator_InvalidId_IsInvalid(int id)
    {
        var validator = new UpdateTransactionCommandValidator();
        var result    = validator.Validate(new UpdateTransactionCommand(id, null, null, null, null, null, null, null, null, false, false));
        Assert.False(result.IsValid);
        Assert.Contains("Id must be a positive integer.", result.Errors);
    }

    [Fact]
    public void UpdateTransactionCommandValidator_WhitespaceDescription_IsInvalid()
    {
        var validator = new UpdateTransactionCommandValidator();
        var result    = validator.Validate(new UpdateTransactionCommand(1, null, null, "   ", null, null, null, null, null, false, false));
        Assert.False(result.IsValid);
        Assert.Contains("Description must not be empty.", result.Errors);
    }

    [Fact]
    public void UpdateTransactionCommandValidator_NegativeAmount_IsInvalid()
    {
        var validator = new UpdateTransactionCommandValidator();
        var result    = validator.Validate(new UpdateTransactionCommand(1, null, null, null, -10m, null, null, null, null, false, false));
        Assert.False(result.IsValid);
        Assert.Contains("Amount must be non-negative.", result.Errors);
    }

    [Theory]
    [InlineData("Credit")]
    [InlineData("DEBIT")]
    public void UpdateTransactionCommandValidator_InvalidType_IsInvalid(string type)
    {
        var validator = new UpdateTransactionCommandValidator();
        var result    = validator.Validate(new UpdateTransactionCommand(1, null, null, null, null, type, null, null, null, false, false));
        Assert.False(result.IsValid);
        Assert.Contains("Type must be 'credit' or 'debit'.", result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateTransactionCommandValidator_InvalidCategoryId_IsInvalid(int categoryId)
    {
        var validator = new UpdateTransactionCommandValidator();
        var result    = validator.Validate(new UpdateTransactionCommand(1, null, null, null, null, null, null, categoryId, null, false, false));
        Assert.False(result.IsValid);
        Assert.Contains("CategoryId must be a positive integer.", result.Errors);
    }

    [Fact]
    public void MarkTransfersCommandValidator_ValidInput_IsValid()
    {
        var validator = new MarkTransfersCommandValidator();
        var result    = validator.Validate(new MarkTransfersCommand([1, 2, 3]));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MarkTransfersCommandValidator_EmptyArray_IsInvalid()
    {
        var validator = new MarkTransfersCommandValidator();
        var result    = validator.Validate(new MarkTransfersCommand([]));
        Assert.False(result.IsValid);
        Assert.Contains("At least one transaction ID is required.", result.Errors);
    }

    [Fact]
    public void MarkTransfersCommandValidator_NullArray_IsInvalid()
    {
        var validator = new MarkTransfersCommandValidator();
        var result    = validator.Validate(new MarkTransfersCommand(null!));
        Assert.False(result.IsValid);
        Assert.Contains("At least one transaction ID is required.", result.Errors);
    }

    [Fact]
    public void MarkTransfersCommandValidator_NonPositiveId_IsInvalid()
    {
        var validator = new MarkTransfersCommandValidator();
        var result    = validator.Validate(new MarkTransfersCommand([1, 0, 3]));
        Assert.False(result.IsValid);
        Assert.Contains("All transaction IDs must be positive integers.", result.Errors);
    }

    [Fact]
    public void GetTransactionsQueryValidator_ValidInput_IsValid()
    {
        var validator = new GetTransactionsQueryValidator();
        var result    = validator.Validate(new GetTransactionsQuery(null, "2024-01", "credit", null, null, 0, 20));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GetTransactionsQueryValidator_NegativeSkip_IsInvalid()
    {
        var validator = new GetTransactionsQueryValidator();
        var result    = validator.Validate(new GetTransactionsQuery(null, null, null, null, null, -1, 20));
        Assert.False(result.IsValid);
        Assert.Contains("Skip must be non-negative.", result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void GetTransactionsQueryValidator_InvalidTake_IsInvalid(int take)
    {
        var validator = new GetTransactionsQueryValidator();
        var result    = validator.Validate(new GetTransactionsQuery(null, null, null, null, null, 0, take));
        Assert.False(result.IsValid);
        Assert.Contains("Take must be between 1 and 500.", result.Errors);
    }

    [Theory]
    [InlineData("2024")]
    [InlineData("24-01")]
    [InlineData("2024-1")]
    [InlineData("January")]
    public void GetTransactionsQueryValidator_InvalidMonthFormat_IsInvalid(string month)
    {
        var validator = new GetTransactionsQueryValidator();
        var result    = validator.Validate(new GetTransactionsQuery(null, month, null, null, null, 0, 20));
        Assert.False(result.IsValid);
        Assert.Contains("Month must be in YYYY-MM format.", result.Errors);
    }

    [Theory]
    [InlineData("Credit")]
    [InlineData("DEBIT")]
    [InlineData("transfer")]
    public void GetTransactionsQueryValidator_InvalidType_IsInvalid(string type)
    {
        var validator = new GetTransactionsQueryValidator();
        var result    = validator.Validate(new GetTransactionsQuery(null, null, type, null, null, 0, 20));
        Assert.False(result.IsValid);
        Assert.Contains("Type must be 'credit' or 'debit'.", result.Errors);
    }
}
