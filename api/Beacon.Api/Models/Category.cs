using Beacon.Api.Features.Shared;

namespace Beacon.Api.Models;

public class Category : IProtectedEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#94a3b8";
    public bool IsProtected { get; set; }

    public ICollection<CategoryRule> Rules { get; set; } = [];
}
