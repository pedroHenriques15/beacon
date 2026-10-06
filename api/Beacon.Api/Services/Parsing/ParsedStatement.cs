namespace Beacon.Api.Services.Parsing;

/// <param name="PprBalance">
/// What the retirement savings plan (PPR) the account holds is worth at the end of the period,
/// counting a redemption until its cash reaches the account (BPI's <c>ACTIVOS</c> less the
/// current account).
/// </param>
/// <param name="BalancesRelative">
/// The source has no balances (a CSV export), so <see cref="OpeningBalance"/> is 0 and every
/// balance counts from it; <c>StatementUploadService</c> shifts them by the previous statement's
/// closing balance.
/// </param>
/// <param name="Warnings">What the parser skipped or assumed, shown with the upload's result.</param>
/// <param name="PprSubscriptions">
/// Money paid into the PPR in the period, from the plan's own section; each becomes a row.
/// </param>
public record ParsedStatement(
    string Bank,
    string? Account,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Currency,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string SourceFile,
    IReadOnlyList<ParsedTransaction> Transactions,
    decimal? PprBalance = null,
    bool BalancesRelative = false,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<ParsedPprSubscription>? PprSubscriptions = null
);

/// <param name="Description">The plan section's wording, for example "SUBSCRICAO EMPRESA".</param>
public record ParsedPprSubscription(
    DateOnly DatePosting,
    DateOnly DateValue,
    string Description,
    decimal Amount
);

/// <param name="Trade">
/// Set when the row buys an investment: the row is cash moved into it, so it is excluded with no
/// category, and the trade becomes an <c>InvestmentLot</c> (ADR-031).
/// </param>
public record ParsedTransaction(
    DateOnly DatePosting,
    DateOnly DateValue,
    string Description,
    decimal Amount,
    string Type,
    decimal Balance,
    ParsedTrade? Trade = null
);

/// <summary>An investment buy found on a statement row.</summary>
/// <param name="AssetName">The name a new asset gets when the ISIN is not known yet.</param>
/// <param name="Fees">Fees and taxes paid on top of <c>Quantity × PricePerUnit</c>, positive.</param>
/// <param name="ExternalId">The source's id of the trade, so a second import never books it twice.</param>
/// <param name="Note">Stored on the lot, saying where it came from.</param>
public record ParsedTrade(
    string Isin,
    string AssetName,
    DateOnly Date,
    decimal Quantity,
    decimal PricePerUnit,
    decimal Fees,
    string? ExternalId,
    string Note
);
