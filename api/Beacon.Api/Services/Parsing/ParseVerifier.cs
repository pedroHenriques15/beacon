namespace Beacon.Api.Services.Parsing;

public static class ParseVerifier
{
    public static IReadOnlyList<string> VerifyStatement(ParsedStatement parsed)
    {
        var warnings = new List<string>();

        if (parsed.Transactions.Count == 0)
        {
            warnings.Add("No transactions were parsed from this statement.");
            return warnings;
        }

        var credit = parsed.Transactions.Where(t => t.Type == "credit").Sum(t => t.Amount);
        var debit  = parsed.Transactions.Where(t => t.Type == "debit").Sum(t => t.Amount);
        var expected = parsed.OpeningBalance + credit - debit;

        if (Math.Abs(expected - parsed.ClosingBalance) > 0.01m)
            warnings.Add(
                $"Balance mismatch: opening {parsed.OpeningBalance:F2} + credits {credit:F2} − debits {debit:F2} = {expected:F2}, " +
                $"but closing balance is {parsed.ClosingBalance:F2}.");

        if (parsed.Transactions.Any(t => t.Type == "unknown" && t.Amount >= 0.01m))
            warnings.Add("One or more transactions could not be classified as credit or debit.");

        return warnings;
    }

    public static IReadOnlyList<string> VerifySalarySlip(ParsedSalarySlip parsed)
    {
        var warnings = new List<string>();

        if (parsed.LineItems.Count == 0)
        {
            warnings.Add("No line items were parsed from this salary slip.");
            return warnings;
        }

        var incomeSum    = parsed.LineItems.Where(li => li.ItemType == "income").Sum(li => li.Amount);
        var deductionSum = parsed.LineItems.Where(li => li.ItemType == "deduction").Sum(li => li.Amount);
        var taxSum       = parsed.LineItems.Where(li => li.ItemType == "tax").Sum(li => li.Amount);

        if (Math.Abs(incomeSum - parsed.GrossAmount) > 0.01m)
            warnings.Add(
                $"Gross amount mismatch: income line items sum to {incomeSum:F2}, " +
                $"but gross amount is {parsed.GrossAmount:F2}.");

        var computed = incomeSum - deductionSum - taxSum;
        if (Math.Abs(computed - parsed.NetAmount) > 0.01m)
            warnings.Add(
                $"Net amount mismatch: income − deductions − tax = {computed:F2}, " +
                $"but net amount is {parsed.NetAmount:F2}.");

        return warnings;
    }

    public static IReadOnlyList<string> VerifyGroceryReceipt(ParsedGroceryReceipt parsed)
    {
        var warnings = new List<string>();

        if (parsed.Items.Count == 0)
        {
            warnings.Add("No items were parsed from this receipt.");
            return warnings;
        }

        var itemSum = parsed.Items.Sum(i => i.Amount);
        if (Math.Abs(itemSum - parsed.Total) > 0.01m)
            warnings.Add(
                $"Total mismatch: item amounts sum to {itemSum:F2}, " +
                $"but receipt total is {parsed.Total:F2}.");

        return warnings;
    }
}
