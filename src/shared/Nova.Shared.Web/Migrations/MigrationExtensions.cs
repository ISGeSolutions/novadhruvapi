using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Migrations;

namespace Nova.Shared.Web.Migrations;

/// <summary>Extension methods for wiring database migrations into an ASP.NET Core service.</summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Registers the <see cref="IMigrationRunner"/> singleton.
    /// Call this in <c>Program.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// Migrations are NOT run automatically at startup.
    /// Trigger them explicitly via <c>POST /admin/migrations/run</c> after each deployment.
    /// </remarks>
    public static WebApplicationBuilder AddNovaMigrations(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IMigrationRunner, TenantMigrationRunner>();
        return builder;
    }
}
