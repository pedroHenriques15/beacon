using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Salary.Queries.GetSalarySlips;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Salary.Commands.CreateSalarySlip;

public record CreateLineItemRequest(
    int SalaryItemCategoryId,
    decimal Amount,
    int SortOrder,
    decimal? Quantity = null,
    decimal? UnitValue = null,
    decimal? Percentage = null,
    decimal? IncidenciaBase = null);

public record CreateSalarySlipCommand(
    int SalaryProfileId,
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    string? Notes,
    string? PdfPath,
    string? SourceFile,
    List<CreateLineItemRequest> LineItems,
    decimal? BaseAmount = null,
    decimal? HoursWorked = null,
    decimal? HourlyRate = null,
    decimal? TotalEspecie = null);

public class CreateSalarySlipCommandHandler(AppDbContext db)
{
    public async Task<(SalarySlipResponse? Result, string? Error)> HandleAsync(
        CreateSalarySlipCommand command, CancellationToken ct = default)
    {
        var profileExists = await db.SalaryProfiles.AnyAsync(p => p.Id == command.SalaryProfileId, ct);
        if (!profileExists) return (null, "Salary profile not found.");

        var duplicate = await db.SalarySlips.AnyAsync(
            s => s.SalaryProfileId == command.SalaryProfileId && s.Period == command.Period, ct);
        if (duplicate) return (null, "A salary slip for this profile and period already exists.");

        var profileCategoryIds = await db.SalaryItemCategories
            .Where(c => c.SalaryProfileId == command.SalaryProfileId)
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var lineItem in command.LineItems)
        {
            if (!profileCategoryIds.Contains(lineItem.SalaryItemCategoryId))
                return (null, $"Category {lineItem.SalaryItemCategoryId} does not belong to profile {command.SalaryProfileId}.");
        }

        var slip = new SalarySlip
        {
            SalaryProfileId = command.SalaryProfileId,
            Period          = command.Period,
            GrossAmount     = command.GrossAmount,
            NetAmount       = command.NetAmount,
            Notes           = command.Notes?.Trim(),
            PdfPath         = command.PdfPath,
            SourceFile      = command.SourceFile,
            ImportedAt      = DateTime.UtcNow,
            BaseAmount      = command.BaseAmount,
            HoursWorked     = command.HoursWorked,
            HourlyRate      = command.HourlyRate,
            TotalEspecie    = command.TotalEspecie,
            LineItems       = command.LineItems.Select(li => new SalaryLineItem
            {
                SalaryItemCategoryId = li.SalaryItemCategoryId,
                Amount               = li.Amount,
                SortOrder            = li.SortOrder,
                Quantity             = li.Quantity,
                UnitValue            = li.UnitValue,
                Percentage           = li.Percentage,
                IncidenciaBase       = li.IncidenciaBase,
            }).ToList()
        };

        db.SalarySlips.Add(slip);
        await db.SaveChangesAsync(ct);

        var result = await db.SalarySlips
            .Include(s => s.SalaryProfile)
            .Include(s => s.LineItems).ThenInclude(li => li.SalaryItemCategory)
            .Where(s => s.Id == slip.Id)
            .Select(s => new SalarySlipResponse(
                s.Id, s.SalaryProfileId, s.SalaryProfile.Name,
                s.Period, s.GrossAmount, s.NetAmount, s.Notes, s.PdfPath, s.SourceFile, s.ImportedAt,
                s.LineItems.OrderBy(li => li.SortOrder)
                    .Select(li => new SalaryLineItemResponse(
                        li.Id, li.SalaryItemCategoryId,
                        li.SalaryItemCategory.Name, li.SalaryItemCategory.Color,
                        li.SalaryItemCategory.ItemType, li.Amount, li.SortOrder,
                        li.Quantity, li.UnitValue, li.Percentage, li.IncidenciaBase))
                    .ToList(),
                s.BaseAmount, s.HoursWorked, s.HourlyRate, s.TotalEspecie))
            .FirstAsync(ct);

        return (result, null);
    }
}
