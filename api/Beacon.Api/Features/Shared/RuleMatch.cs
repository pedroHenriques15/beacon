namespace Beacon.Api.Features.Shared;

/// <summary>
/// How a category rule (transaction or grocery) matches a row, the same at import and when a rule
/// is created. Its text matches when the row's description, trimmed, equals it (a rule that
/// matches the whole description) or contains it (any other rule); both are ordinal, so
/// case-sensitive, and patterns are trimmed on save. When the rule has an amount, the row's amount
/// must equal it too. An empty pattern is no text condition; a rule with neither matches nothing.
/// The client's <c>matchesRule</c> (<c>core/utils/rule-match.ts</c>) mirrors this for the dialogs'
/// match count.
/// </summary>
public static class RuleMatch
{
    public static bool Matches(
        string? pattern, bool matchWholeDescription, decimal? value, string description, decimal amount)
    {
        var hasPattern = !string.IsNullOrEmpty(pattern);
        if (!hasPattern && value is null)
            return false;

        var patternOk = !hasPattern || (matchWholeDescription
            ? string.Equals(description.Trim(), pattern, StringComparison.Ordinal)
            : description.Contains(pattern!, StringComparison.Ordinal));
        var valueOk = value is null || amount == value.Value;
        return patternOk && valueOk;
    }
}
