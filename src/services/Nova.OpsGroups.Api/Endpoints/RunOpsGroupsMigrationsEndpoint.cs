using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.Shared.Messaging;
using Nova.Shared.Migrations;
using Nova.Shared.Tenancy;
using Nova.Shared.Web.Migrations;

namespace Nova.OpsGroups.Api.Endpoints;

public static class RunOpsGroupsMigrationsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/admin/migrations/run", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IOptions<OpsGroupsDbSettings> opsGroupsDbOptions,
        IMigrationRunner              runner,
        CancellationToken             ct)
    {
        OpsGroupsDbSettings opsGroupsDb = opsGroupsDbOptions.Value;

        var opsGroupsTenant = new TenantRecord
        {
            TenantId         = "nova-opsgroups",
            DisplayName      = "Nova OpsGroups Database",
            DbType           = opsGroupsDb.DbType,
            ConnectionString = opsGroupsDb.ConnectionString,
            SchemaVersion    = "v1",
            BrokerType       = BrokerType.RabbitMq
        };

        MigrationSummary summary = await runner.RunAsync(opsGroupsTenant, typeof(Program).Assembly, ct);

        return Results.Ok(new
        {
            tenant_id       = summary.TenantId,
            applied         = summary.Applied,
            blocked         = summary.Blocked,
            blocked_scripts = summary.BlockedScripts.Select(b => new { name = b.Name, reasons = b.Reasons }),
        });
    }
}
