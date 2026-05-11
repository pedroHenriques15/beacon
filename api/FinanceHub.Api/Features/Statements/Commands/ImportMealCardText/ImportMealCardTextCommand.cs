namespace FinanceHub.Api.Features.Statements.Commands.ImportMealCardText;

public record ImportMealCardTextCommand(
    string RawText,
    DateOnly? PeriodFrom = null,
    DateOnly? PeriodTo = null,
    decimal? ClosingBalance = null);
