using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Shared;

public interface IProtectedEntity
{
    bool IsProtected { get; }
}

public static class ProtectedEntityHelper
{
    public static async Task<(bool Found, bool IsProtected)> DeleteIfAllowedAsync<T>(
        DbContext db,
        DbSet<T> set,
        int id,
        CancellationToken ct) where T : class, IProtectedEntity
    {
        var entity = await set.FindAsync([id], ct);
        if (entity is null) return (false, false);
        if (entity.IsProtected) return (true, true);
        set.Remove(entity);
        await db.SaveChangesAsync(ct);
        return (true, false);
    }
}
