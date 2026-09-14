using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Security;

internal static class MutationLocks
{
    public static Task DeviceAsync(DisplayControlDbContext db, Guid tenantId, Guid deviceId, CancellationToken ct) =>
        AcquireAsync(db, $"device:{tenantId:N}:{deviceId:N}", ct);

    public static Task SchedulingAsync(DisplayControlDbContext db, Guid tenantId, CancellationToken ct) =>
        AcquireAsync(db, $"scheduling:{tenantId:N}", ct);

    private static Task<int> AcquireAsync(DisplayControlDbContext db, string key, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Mutation locks require an active transaction.");
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=-1", ct);
    }
}
