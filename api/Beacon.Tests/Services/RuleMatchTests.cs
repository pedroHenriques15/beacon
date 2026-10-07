using Beacon.Api.Features.Shared;

namespace Beacon.Tests.Services;

public class RuleMatchTests
{
    private const bool Whole = true;
    private const bool Partial = false;

    [Fact]
    public void Matches_Whole_TheWholeDescriptionOnly()
    {
        Assert.True(RuleMatch.Matches("LIDL Lisboa", Whole, null, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches("LIDL", Whole, null, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches("Lisboa", Whole, null, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches("LIDL Lisboa 2", Whole, null, "LIDL Lisboa", 30m));
    }

    [Fact]
    public void Matches_Whole_IgnoresSpacesAroundTheDescription()
    {
        Assert.True(RuleMatch.Matches("LIDL Lisboa", Whole, null, "  LIDL Lisboa ", 30m));
        Assert.False(RuleMatch.Matches("LIDL Lisboa", Whole, null, "LIDL  Lisboa", 30m));
    }

    [Fact]
    public void Matches_Partial_AnyPartOfTheDescription()
    {
        Assert.True(RuleMatch.Matches("LIDL", Partial, null, "LIDL Lisboa", 30m));
        Assert.True(RuleMatch.Matches("Lisboa", Partial, null, "LIDL Lisboa", 30m));
        Assert.True(RuleMatch.Matches("LIDL Lisboa", Partial, null, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches("CONTINENTE", Partial, null, "LIDL Lisboa", 30m));
    }

    [Theory]
    [InlineData(Whole)]
    [InlineData(Partial)]
    public void Matches_Text_IsCaseSensitive(bool matchWholeDescription)
    {
        Assert.False(RuleMatch.Matches("lidl lisboa", matchWholeDescription, null, "LIDL Lisboa", 30m));
    }

    [Theory]
    [InlineData(Whole)]
    [InlineData(Partial)]
    public void Matches_AmountOnly_AnyDescriptionOfThatAmount(bool matchWholeDescription)
    {
        Assert.True(RuleMatch.Matches(null, matchWholeDescription, 30m, "LIDL Lisboa", 30m));
        Assert.True(RuleMatch.Matches(string.Empty, matchWholeDescription, 30m, "OTHER STORE", 30.00m));
        Assert.False(RuleMatch.Matches(string.Empty, matchWholeDescription, 30m, "LIDL Lisboa", 30.01m));
    }

    [Fact]
    public void Matches_TextAndAmount_BothMustHold()
    {
        Assert.True(RuleMatch.Matches("LIDL Lisboa", Whole, 30m, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches("LIDL Lisboa", Whole, 30m, "LIDL Lisboa", 99m));
        Assert.False(RuleMatch.Matches("LIDL Lisboa", Whole, 30m, "OTHER STORE", 30m));
        Assert.True(RuleMatch.Matches("LIDL", Partial, 30m, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches("LIDL", Partial, 30m, "LIDL Lisboa", 99m));
    }

    [Theory]
    [InlineData(Whole)]
    [InlineData(Partial)]
    public void Matches_Neither_MatchesNothing(bool matchWholeDescription)
    {
        Assert.False(RuleMatch.Matches(null, matchWholeDescription, null, "LIDL Lisboa", 30m));
        Assert.False(RuleMatch.Matches(string.Empty, matchWholeDescription, null, string.Empty, 0m));
    }
}
