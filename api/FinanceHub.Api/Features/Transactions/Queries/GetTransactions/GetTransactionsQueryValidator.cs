using System.Text.RegularExpressions;
using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.Transactions.Queries.GetTransactions;

public class GetTransactionsQueryValidator
{
    private static readonly Regex MonthRegex = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    public ValidationResult Validate(GetTransactionsQuery query)
    {
        var errors = new List<string>();

        if (query.Skip < 0)
            errors.Add("Skip must be non-negative.");

        if (query.Take < 1 || query.Take > 500)
            errors.Add("Take must be between 1 and 500.");

        if (query.Month is not null && !MonthRegex.IsMatch(query.Month))
            errors.Add("Month must be in YYYY-MM format.");

        if (query.Type is not null && query.Type != "credit" && query.Type != "debit")
            errors.Add("Type must be 'credit' or 'debit'.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
