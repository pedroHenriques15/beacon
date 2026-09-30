using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
using Beacon.Api.Features.Salary.Queries.GetSalarySlips;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Salary.Commands.MergeSalarySlip;

public class MergeSalarySlipCommandHandler(
    AppDbContext db,
    FileStorageService fileStorage,
    ILogger<MergeSalarySlipCommandHandler> logger)
{
    private const int SourceFileMaxLength = 500;

    public async Task<(SalarySlipResponse? Result, string? Error)> HandleAsync(
        MergeSalarySlipCommand command, CancellationToken ct = default)
    {
        var slip = await db.SalarySlips
            .Include(s => s.LineItems)
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (slip is null) return (null, null);

        // Per-profile category isolation (mirrors CreateSalarySlip): every incoming line item must
        // reference a category belonging to the target slip's profile.
        var profileCategoryIds = await db.SalaryItemCategories
            .Where(c => c.SalaryProfileId == slip.SalaryProfileId)
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var lineItem in command.LineItems)
        {
            if (!profileCategoryIds.Contains(lineItem.SalaryItemCategoryId))
                return (null, $"Category {lineItem.SalaryItemCategoryId} does not belong to profile {slip.SalaryProfileId}.");
        }

        // The hourly rate is a rate, not a total: weight it by hours so a half-month at a different
        // FX rate cannot skew the month. Falls back to whichever side actually has a rate.
        slip.HourlyRate = MergeHourlyRate(slip, command);

        slip.GrossAmount  += command.GrossAmount;
        slip.NetAmount    += command.NetAmount;
        slip.BaseAmount    = AddNullable(slip.BaseAmount, command.BaseAmount);
        slip.HoursWorked   = AddNullable(slip.HoursWorked, command.HoursWorked);
        slip.TotalEspecie  = AddNullable(slip.TotalEspecie, command.TotalEspecie);
        slip.Notes         = MergeNotes(slip.Notes, command.Notes);
        slip.SourceFile    = MergeSourceFile(slip.SourceFile, command.SourceFile);

        MergeLineItems(slip, command.LineItems);
        MergePdf(slip, command.PdfPath);

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

    /// <summary>Line items combine per category, so one month yields one "Base Pay" line, not one per invoice.</summary>
    private static void MergeLineItems(SalarySlip slip, List<CreateLineItemRequest> incoming)
    {
        var nextSortOrder = slip.LineItems.Count == 0 ? 0 : slip.LineItems.Max(li => li.SortOrder) + 1;

        foreach (var item in incoming)
        {
            var existing = slip.LineItems.FirstOrDefault(li => li.SalaryItemCategoryId == item.SalaryItemCategoryId);
            if (existing is null)
            {
                slip.LineItems.Add(new SalaryLineItem
                {
                    SalaryItemCategoryId = item.SalaryItemCategoryId,
                    Amount               = item.Amount,
                    SortOrder            = nextSortOrder++,
                    Quantity             = item.Quantity,
                    UnitValue            = item.UnitValue,
                    Percentage           = item.Percentage,
                    IncidenciaBase       = item.IncidenciaBase,
                });
                continue;
            }

            existing.Amount  += item.Amount;
            existing.Quantity = AddNullable(existing.Quantity, item.Quantity);
            // Per-unit detail only survives when both halves agree on it; otherwise it would describe
            // neither of them once the amounts are summed.
            existing.UnitValue      = KeepIfEqual(existing.UnitValue, item.UnitValue);
            existing.Percentage     = KeepIfEqual(existing.Percentage, item.Percentage);
            existing.IncidenciaBase = AddNullable(existing.IncidenciaBase, item.IncidenciaBase);
        }
    }

    /// <summary>
    /// A slip holds a single PDF. The first one imported stays authoritative; a second PDF is dropped
    /// from storage rather than left behind unreferenced (its name is kept in <c>SourceFile</c>).
    /// </summary>
    private void MergePdf(SalarySlip slip, string? incomingPdfPath)
    {
        if (incomingPdfPath is null) return;

        if (slip.PdfPath is null)
        {
            slip.PdfPath = FileStorageService.FileNameOf(incomingPdfPath);
            return;
        }

        // By name: one side may still be a full path written before PDF paths became file names.
        if (FileStorageService.FileNameOf(slip.PdfPath) == FileStorageService.FileNameOf(incomingPdfPath)) return;

        try { fileStorage.Delete(incomingPdfPath); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete superseded PDF {Path} while merging into salary slip {Id}",
                incomingPdfPath, slip.Id);
        }
    }

    private static decimal? MergeHourlyRate(SalarySlip slip, MergeSalarySlipCommand command)
    {
        if (slip.HourlyRate is not { } existingRate) return command.HourlyRate;
        if (command.HourlyRate is not { } incomingRate) return existingRate;

        var existingHours = slip.HoursWorked ?? 0m;
        var incomingHours = command.HoursWorked ?? 0m;
        var totalHours    = existingHours + incomingHours;
        if (totalHours <= 0m) return Math.Round((existingRate + incomingRate) / 2m, 2, MidpointRounding.AwayFromZero);

        return Math.Round(
            (existingRate * existingHours + incomingRate * incomingHours) / totalHours,
            2, MidpointRounding.AwayFromZero);
    }

    private static string? MergeNotes(string? existing, string? incoming)
    {
        var addition = incoming?.Trim();
        if (string.IsNullOrEmpty(addition)) return existing;
        if (string.IsNullOrWhiteSpace(existing)) return Truncate(addition, 1000);
        if (existing.Contains(addition, StringComparison.OrdinalIgnoreCase)) return existing;
        return Truncate($"{existing}\n{addition}", 1000);
    }

    private static string? MergeSourceFile(string? existing, string? incoming)
    {
        var addition = incoming?.Trim();
        if (string.IsNullOrEmpty(addition)) return existing;
        if (string.IsNullOrWhiteSpace(existing)) return Truncate(addition, SourceFileMaxLength);

        var parts = existing.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Contains(addition, StringComparer.OrdinalIgnoreCase)) return existing;

        return Truncate($"{existing}; {addition}", SourceFileMaxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static decimal? AddNullable(decimal? a, decimal? b) =>
        a is null ? b : b is null ? a : a + b;

    private static decimal? KeepIfEqual(decimal? a, decimal? b) => a == b ? a : null;
}
