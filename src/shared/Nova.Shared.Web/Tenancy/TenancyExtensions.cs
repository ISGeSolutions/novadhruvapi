using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Data;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Web.Tenancy;

/// <summary>DI registration extensions for tenancy services.</summary>
public static class TenancyExtensions
{
    private const string TenantContextKey = "Nova.TenantContext";

    /// <summary>
    /// Registers <see cref="TenantRegistry"/> as singleton and <see cref="TenantContext"/>
    /// as a scoped service resolved from <c>HttpContext.Items</c> (set by middleware).
    /// </summary>
    public static IServiceCollection AddNovaTenancy(this IServiceCollection services)
    {
        services.AddSingleton<TenantRegistry>();
        services.AddHttpContextAccessor();

        services.AddScoped<TenantContext>(sp =>
        {
            IHttpContextAccessor accessor = sp.GetRequiredService<IHttpContextAccessor>();
            HttpContext? httpContext = accessor.HttpContext;

            if (httpContext?.Items[TenantContextKey] is TenantContext ctx)
                return ctx;

            throw new InvalidOperationException(
                "TenantContext has not been resolved for this request. " +
                "Ensure TenantResolutionMiddleware runs before accessing tenant-scoped services.");
        });

        // Resolves the correct SQL dialect for the current tenant's database engine.
        // Depends on TenantContext — must run after TenantResolutionMiddleware.
        services.AddScoped<ISqlDialect>(sp =>
        {
            TenantContext ctx = sp.GetRequiredService<TenantContext>();
            return ctx.DbType switch
            {
                DbType.Postgres => new PostgresDialect(),
                DbType.MariaDb  => new MariaDbDialect(),
                _               => new MsSqlDialect()   // DbType.MsSql and any future engine defaults to MSSQL
            };
        });

        return services;
    }

    /// <summary>Key used to store <see cref="TenantContext"/> in <c>HttpContext.Items</c>.</summary>
    public static string ContextItemKey => TenantContextKey;
}
