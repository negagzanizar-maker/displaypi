using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace DisplayControl.Api.Security;

public sealed class TenantTransactionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ScopedTenantContext tenantContext,
        DisplayControlDbContext dbContext)
    {
        // Static SPA fallbacks and health endpoints do not execute tenant use cases.
        if (tenantContext.TenantId is not Guid tenantId ||
            context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
        {
            await next(context);
            return;
        }

        if (context.GetEndpoint()?.Metadata.GetMetadata<SkipTenantTransactionAttribute>() is not null)
        {
            await next(context);
            return;
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await next(context);
            return;
        }

        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, context.RequestAborted);
        context.Items[TenantTransactionCommitFilter.TransactionKey] = transaction;
        try
        {
            await next(context);
            // MVC completes transactions before executing its result. An endpoint that
            // bypasses MVC must not acknowledge a write before committing it.
            if (context.Items.ContainsKey(TenantTransactionCommitFilter.TransactionKey))
            {
                if (context.Response.HasStarted)
                    throw new InvalidOperationException("A tenant endpoint started its response before completing its transaction.");
                await transaction.CommitAsync(context.RequestAborted);
            }
        }
        finally
        {
            context.Items.Remove(TenantTransactionCommitFilter.TransactionKey);
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipTenantTransactionAttribute : Attribute;
