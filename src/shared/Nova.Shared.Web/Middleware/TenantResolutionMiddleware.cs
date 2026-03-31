using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nova.Shared.Tenancy;
using Nova.Shared.Web.Tenancy;

namespace Nova.Shared.Web.Middleware;

/// <summary>
/// Resolves the <see cref="TenantContext"/> from authenticated JWT claims and stores
/// it in <c>HttpContext.Items</c> for scoped DI resolution.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initialises the middleware.</summary>
    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Processes the request.</summary>
    public async Task InvokeAsync(HttpContext context, TenantRegistry registry)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            string? tenantId = context.User.FindFirstValue("tenant_id");

            if (!string.IsNullOrEmpty(tenantId) && registry.TryGetTenant(tenantId, out TenantRecord? record) && record is not null)
            {
                TenantContext tenantContext = new()
                {
                    TenantId = record.TenantId,
                    ConnectionString = record.ConnectionString,
                    DbType = record.DbType,
                    SchemaVersion = record.SchemaVersion
                };

                context.Items[TenancyExtensions.ContextItemKey] = tenantContext;
            }
        }

        await _next(context);
    }
}
