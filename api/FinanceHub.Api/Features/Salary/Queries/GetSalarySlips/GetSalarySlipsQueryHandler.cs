using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Salary.Queries.GetSalarySlips;

public record SalaryLineItemResponse(
    int Id,
    int SalaryItemCategoryId,
    string CategoryName,
    string CategoryColor,
    string CategoryItemType,
    decimal Amount,
    int SortOrder,
    decimal? Quantity,
    decimal? UnitValue,
    decimal? Percentage,
    decimal? IncidenciaBase);

public record SalarySlipResponse(
    int Id,
    int SalaryProfileId,
    string ProfileName,
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    string? Notes,
    string? PdfPath,
    string? SourceFile,
    DateTime ImportedAt,
    List<SalaryLineItemResponse> LineItems,
    decimal? BaseAmount,
    decimal? HoursWorked,
    decimal? HourlyRate,
    decimal? TotalEspecie);

public record GetSalarySlipsQuery(int? ProfileId, int? Year);

public class GetSalarySlipsQueryHandler(AppDbContext db)
{
    public async Task<List<SalarySlipResponse>> HandleAsync(GetSalarySlipsQuery query, CancellationToken ct = default)
    {
        var q = db.SalarySlips
            .Include(s => s.SalaryProfile)
            .Include(s => s.LineItems)
                .ThenInclude(li => li.SalaryItemCategory)
            .AsQueryable();

        if (query.ProfileId.HasValue)
            q = q.Where(s => s.SalaryProfileId == query.ProfileId.Value);

        if (query.Year.HasValue)
            q = q.Where(s => s.Period.Year == query.Year.Value);

        return await q
            .OrderByDescending(s => s.Period)
            .Select(s => new SalarySlipResponse(
                s.Id,
                s.SalaryProfileId,
                s.SalaryProfile.Name,
                s.Period,
                s.GrossAmount,
                s.NetAmount,
                s.Notes,
                s.PdfPath,
                s.SourceFile,
                s.ImportedAt,
                s.LineItems
                    .OrderBy(li => li.SortOrder)
                    .Select(li => new SalaryLineItemResponse(
                        li.Id,
                        li.SalaryItemCategoryId,
                        li.SalaryItemCategory.Name,
                        li.SalaryItemCategory.Color,
                        li.SalaryItemCategory.ItemType,
                        li.Amount,
                        li.SortOrder,
                        li.Quantity,
                        li.UnitValue,
                        li.Percentage,
                        li.IncidenciaBase))
                    .ToList(),
                s.BaseAmount,
                s.HoursWorked,
                s.HourlyRate,
                s.TotalEspecie))
            .ToListAsync(ct);
    }
}
