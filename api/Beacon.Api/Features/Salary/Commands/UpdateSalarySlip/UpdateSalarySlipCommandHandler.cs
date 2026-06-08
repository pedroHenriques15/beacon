using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
using Beacon.Api.Features.Salary.Queries.GetSalarySlips;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Salary.Commands.UpdateSalarySlip;

public record UpdateSalarySlipCommand(
    int Id,
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    string? Notes,
    List<CreateLineItemRequest> LineItems);

public class UpdateSalarySlipCommandHandler(AppDbContext db)
{
    public async Task<(SalarySlipResponse? Result, string? Error)> HandleAsync(
        UpdateSalarySlipCommand command, CancellationToken ct = default)
    {
        var slip = await db.SalarySlips
            .Include(s => s.LineItems)
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (slip is null) return (null, null);

        var duplicate = await db.SalarySlips.AnyAsync(
            s => s.SalaryProfileId == slip.SalaryProfileId
              && s.Period == command.Period
              && s.Id != command.Id, ct);
        if (duplicate) return (null, "A salary slip for this profile and period already exists.");

        slip.Period      = command.Period;
        slip.GrossAmount = command.GrossAmount;
        slip.NetAmount   = command.NetAmount;
        slip.Notes       = command.Notes?.Trim();

        db.SalaryLineItems.RemoveRange(slip.LineItems);
        slip.LineItems = command.LineItems.Select(li => new SalaryLineItem
        {
            SalaryItemCategoryId = li.SalaryItemCategoryId,
            Amount               = li.Amount,
            SortOrder            = li.SortOrder,
            Quantity             = li.Quantity,
            UnitValue            = li.UnitValue,
            Percentage           = li.Percentage,
            IncidenciaBase       = li.IncidenciaBase,
        }).ToList();

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
