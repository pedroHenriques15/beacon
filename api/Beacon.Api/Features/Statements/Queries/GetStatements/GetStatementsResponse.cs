namespace Beacon.Api.Features.Statements.Queries.GetStatements;

public record GetStatementsResponse(
    int Id, string Bank, string Account,
    DateOnly PeriodFrom, DateOnly PeriodTo,
    string Currency, decimal OpeningBalance, decimal ClosingBalance,
    string SourceFile, bool HasFile, int TransactionCount);
