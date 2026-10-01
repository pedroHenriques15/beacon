namespace Beacon.Api.Services.Parsing;

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
    decimal? PprBalance = null
);

public record ParsedTransaction(
    DateOnly DatePosting,
    DateOnly DateValue,
    string Description,
    decimal Amount,
    string Type,
    decimal Balance
);
