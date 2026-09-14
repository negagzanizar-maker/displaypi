using DisplayControl.Api.Realtime;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisplayControl.Api.Security;

/// <summary>Completes tenant writes before any response body, including streamed media, is sent.</summary>
public sealed class TenantTransactionCommitFilter(
    DeviceStateChangeNotifications notifications) : IAsyncAlwaysRunResultFilter, IOrderedFilter
{
    internal static readonly object TransactionKey = new();
    public int Order => int.MaxValue;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Items.TryGetValue(TransactionKey, out var value) && value is IDbContextTransaction transaction)
        {
            // Rejected authentication attempts intentionally persist security evidence.
            await transaction.CommitAsync(context.HttpContext.RequestAborted);
            context.HttpContext.Items.Remove(TransactionKey);
        }

        notifications.FlushCommitted();
        await next();
    }
}
