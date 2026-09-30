using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

/// <summary>
/// The EUR payout half of a micro1 paycheck: a Deel "Confirmation Statement" giving the source USD
/// amount, the exchange fee, the USD→EUR rate and the EUR total that actually reached the bank.
/// Carries no breakdown of its own — it is paired with a <see cref="Micro1InvoiceParser"/> invoice by
/// <see cref="Micro1Reconciler"/>. Plain class (not an <see cref="ISalarySlipParser"/>).
/// </summary>
public partial class DeelWithdrawalParser
{
    public bool CanParse(string fullText) =>
        fullText.Contains("Deel transaction ID") && fullText.Contains("Total sent");

    public DeelWithdrawal Parse(IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var sourceAmount = AmountUtils.ParseUsd(Require(SourceAmountRegex(), fullText, "Source amount"));
        var exchangeRate = decimal.Parse(
            Require(ExchangeRateRegex(), fullText, "Exchange rate"), CultureInfo.InvariantCulture);
        var totalEur     = AmountUtils.ParseUsd(Require(TotalSentRegex(), fullText, "Total sent"));

        var feeMatch    = ExchangeFeeRegex().Match(fullText);
        var exchangeFee = feeMatch.Success ? AmountUtils.ParseUsd(feeMatch.Groups[1].Value) : 0m;

        return new DeelWithdrawal(sourceAmount, exchangeFee, exchangeRate, totalEur);
    }

    private static string Require(Regex regex, string fullText, string label)
    {
        var m = regex.Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException($"Could not find '{label}' in Deel withdrawal statement.");
        return m.Groups[1].Value;
    }

    [GeneratedRegex(@"Source amount\s*\$([\d,]+\.\d{2})")]
    private static partial Regex SourceAmountRegex();

    [GeneratedRegex(@"Exchange fees\s*-?\$([\d,]+\.\d{2})")]
    private static partial Regex ExchangeFeeRegex();

    [GeneratedRegex(@"1\.00 USD\s*=\s*([\d.]+)\s*EUR")]
    private static partial Regex ExchangeRateRegex();

    [GeneratedRegex(@"Total sent\s*€([\d,]+\.\d{2})")]
    private static partial Regex TotalSentRegex();
}

public record DeelWithdrawal(
    decimal SourceAmountUsd,
    decimal ExchangeFeeUsd,
    decimal ExchangeRate,
    decimal TotalEur);
