using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.Shared.Messaging;
using Nova.Shared.Migrations;
using Nova.Shared.Tenancy;
using Nova.Shared.Web.Migrations;

namespace Nova.CommonUX.Api.Endpoints;

/// <summary>
/// Admin endpoint — runs pending DbUp migrations for the <c>nova_auth</c> database.
///
/// <para><b>Routes</b></para>
/// <list type="bullet">
///   <item><c>POST /admin/migrations/run</c></item>
/// </list>
///
/// Creates a synthetic <see cref="TenantRecord"/> from <c>AuthDbSettings</c> so the
/// existing <see cref="IMigrationRunner"/> can be reused without modification.
/// </summary>
public static class RunAuthMigrationsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/admin/migrations/run", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IOptions<AuthDbSettings> authDbOptions,
        IMigrationRunner         runner,
        CancellationToken        ct)
    {
        AuthDbSettings authDb = authDbOptions.Value;

        // Synthetic tenant record — represents the single nova_auth database
        var authTenant = new TenantRecord
        {
            TenantId         = "nova-auth",
            DisplayName      = "Nova Auth Database",
            DbType           = authDb.DbType,
            ConnectionString = authDb.ConnectionString,
            SchemaVersion    = "v1",
            BrokerType       = BrokerType.RabbitMq
        };

        MigrationSummary summary = await runner.RunAsync(authTenant, typeof(Program).Assembly, ct);

        return Results.Ok(new
        {
            tenant_id       = summary.TenantId,
            applied         = summary.Applied,
            blocked         = summary.Blocked,
            blocked_scripts = summary.BlockedScripts.Select(b => new { name = b.Name, reasons = b.Reasons }),
        });
    }
}
