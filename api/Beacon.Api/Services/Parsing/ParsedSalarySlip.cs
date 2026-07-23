namespace Beacon.Api.Services.Parsing;

public record ParsedSalarySlip(
    string Employer,
    string? EmployerNif,
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    IReadOnlyList<ParsedSalaryLineItem> LineItems,
    decimal? BaseAmount = null,
    decimal? HoursWorked = null,
    decimal? HourlyRate = null,
    decimal? TotalEspecie = null);

public record ParsedSalaryLineItem(
    string Description,
    decimal Amount,
    string ItemType,
    decimal? Quantity = null,
    decimal? UnitValue = null,
    decimal? Percentage = null,
    decimal? IncidenciaBase = null);
