using Beacon.Api.Features.Shared;

namespace Beacon.Api.Models;

public class SalaryItemCategory : IProtectedEntity
{
    public int Id { get; set; }
    public int SalaryProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#94a3b8";
    public string ItemType { get; set; } = "income";
    public bool IsProtected { get; set; }
    public SalaryProfile SalaryProfile { get; set; } = null!;
    public ICollection<SalaryLineItem> LineItems { get; set; } = [];
}
