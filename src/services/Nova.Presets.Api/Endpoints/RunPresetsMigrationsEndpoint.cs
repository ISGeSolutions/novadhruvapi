using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Messaging;
using Nova.Shared.Migrations;
using Nova.Shared.Tenancy;
using Nova.Shared.Web.Migrations;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Admin endpoint — runs pending DbUp migrations for the <c>presets</c> database.
///
/// <para><b>Route</b></para>
/// <c>POST /admin/migrations/run</c>
///
/// Creates a synthetic <see cref="TenantRecord"/> from <c>PresetsDbSettings</c> so the
/// existing <see cref="IMigrationRunner"/> can be reused without modification.
/// </summary>
public static class RunPresetsMigrationsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/admin/migrations/run", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IOptions<PresetsDbSettings> presetsDbOptions,
        IMigrationRunner            runner,
        CancellationToken           ct)
    {
        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        var presetsTenant = new TenantRecord
        {
            TenantId         = "nova-presets",
            DisplayName      = "Nova Presets Database",
            DbType           = presetsDb.DbType,
            ConnectionString = presetsDb.ConnectionString,
            SchemaVersion    = "v1",
            BrokerType       = BrokerType.RabbitMq
        };

        MigrationSummary summary = await runner.RunAsync(presetsTenant, typeof(Program).Assembly, ct);

        return Results.Ok(new
        {
            tenant_id       = summary.TenantId,
            applied         = summary.Applied,
            blocked         = summary.Blocked,
            blocked_scripts = summary.BlockedScripts.Select(b => new { name = b.Name, reasons = b.Reasons }),
        });
    }
}
