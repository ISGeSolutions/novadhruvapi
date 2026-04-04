using Nova.Shared.Migrations;
using Nova.Shared.Tenancy;
using Nova.Shared.Web.Migrations;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Admin endpoint — runs pending DbUp migrations for all tenants (or a single tenant).
/// </summary>
/// <remarks>
/// Migrations are NOT run automatically at startup. Call this endpoint explicitly
/// after a deployment to apply pending SQL scripts.
///
/// <para><b>Routes</b></para>
/// <list type="bullet">
///   <item><c>POST /admin/migrations/run</c> — run for ALL tenants</item>
///   <item><c>POST /admin/migrations/run?tenantId=BLDK</c> — run for one tenant only</item>
/// </list>
/// </remarks>
public static class RunMigrationsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/admin/migrations/run", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string?           tenantId,
        TenantRegistry    tenants,
        IMigrationRunner  runner,
        CancellationToken ct)
    {
        IEnumerable<TenantRecord> targets = tenantId is not null
            ? tenants.All.Where(t => t.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
            : tenants.All;

        var summaries = new List<object>();

        foreach (TenantRecord tenant in targets)
        {
            MigrationSummary summary = await runner.RunAsync(tenant, typeof(Program).Assembly, ct);

            summaries.Add(new
            {
                tenant_id       = summary.TenantId,
                applied         = summary.Applied,
                blocked         = summary.Blocked,
                blocked_scripts = summary.BlockedScripts.Select(b => new
                {
                    name    = b.Name,
                    reasons = b.Reasons,
                }),
            });
        }

        return summaries.Count == 0
            ? Results.NotFound(new { error = $"No tenant found with id '{tenantId}'." })
            : Results.Ok(new { migrations = summaries });
    }
}
