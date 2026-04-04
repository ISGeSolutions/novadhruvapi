using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.Shell.Api.Endpoints;

/// <summary>Maps the <c>GET /test-db/mssql</c> diagnostic endpoint.</summary>
public static class TestDbMsSqlEndpoint
{
    /// <summary>Registers the endpoint on the given <see cref="WebApplication"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/test-db/mssql", async (
            IDbConnectionFactory connectionFactory,
            IOptions<AppSettings> appOptions) =>
        {
            AppSettings settings = appOptions.Value;
            MsSqlDialect dialect = new();
            string tableRef = dialect.TableRef("sales97", "pointer");

            try
            {
                if (!settings.DiagnosticConnections.MsSql.Enabled)
                    return Results.Json(new { error = "MSSQL diagnostic connection is disabled.", db = "mssql" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                using IDbConnection connection = connectionFactory.CreateFromConnectionString(
                    settings.DiagnosticConnections.MsSql.ConnectionString,
                    NovaDbType.MsSql);

                // Both crdate and [update] are legacy MSSQL datetime columns.
                // ADO.NET returns datetime as DateTime (Kind = Unspecified) for both.
                // Dapper cannot map datetime directly to DateOnly — both must be DateTime in the DTO.
                // The semantic split is applied in the projection below:
                //
                //   DepDate  (crdate)   → calendar date — time component discarded via DateOnly.FromDateTime()
                //                         SQL alias must match C# constructor parameter name exactly (Dapper rule).
                //                         Wire: "2026-08-15"  (no time, no offset — never shifts with browser locale)
                //
                //   UpdatedOn ([update]) → UTC timestamp — treated as UTC via DateTime.SpecifyKind(Utc)
                //                         then wrapped as DateTimeOffset with zero offset.
                //                         SQL alias must match C# constructor parameter name exactly (Dapper rule).
                //                         Wire: "2026-04-03T10:00:00Z"
                //                         UX sends the datetime shifted to UTC/GMT before calling the API.
                string sql = "SELECT code, value, crdate DepDate, [update] UpdatedOn FROM " + tableRef;
                IEnumerable<PointerRow> rows = await connection.QueryAsync<PointerRow>(sql);

                // Projection — same source type (datetime), different semantic treatment:
                //   DepDate:   DateOnly.FromDateTime()        — drops the time component entirely
                //   UpdatedOn: DateTime.SpecifyKind(Utc)      — stamps UTC kind (no value change)
                //              then new DateTimeOffset(...)   — wraps as +00:00
                IEnumerable<PointerResponse> response = rows.Select(r => new PointerResponse(
                    Code:      r.Code,
                    Value:     r.Value,
                    DepDate:   DateOnly.FromDateTime(r.DepDate),
                    UpdatedOn: new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedOn, DateTimeKind.Utc))));

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = ex.Message, db = "mssql" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
        .WithName("TestDbMsSql");
    }

    // DATE HANDLING REFERENCE — legacy datetime columns, two different semantic treatments
    //
    // Source columns: crdate datetime, [update] datetime
    // ADO.NET returns both as DateTime (Kind = Unspecified).
    // Dapper cannot map datetime → DateOnly directly; both must land as DateTime in the DTO.
    //
    // PointerRow — raw Dapper DTO (mirrors what the DB driver returns):
    //   dep_date   datetime → DateTime   (will be truncated to date only in projection)
    //   updated_on datetime → DateTime   (will be treated as UTC in projection)
    //
    // PointerResponse — wire format (what the client receives):
    //   dep_date   → "2026-08-15"                  DateOnly  → yyyy-MM-dd, no time, no offset
    //                                               Never shifts with browser locale.
    //   updated_on → "2026-04-03T10:00:00+00:00"   DateTimeOffset UTC → ISO 8601 with +00:00
    //                                               UX shifts to local time for display;
    //                                               UX shifts back to UTC before sending to API.

    private sealed record PointerRow(
        string   Code,
        string   Value,
        DateTime DepDate,    // datetime column — time component will be dropped
        DateTime UpdatedOn); // datetime column — treated as UTC, Kind = Unspecified from ADO.NET

    private sealed record PointerResponse(
        string         Code,
        string         Value,
        DateOnly       DepDate,    // wire: "2026-08-15"
        DateTimeOffset UpdatedOn); // wire: "2026-04-03T10:00:00+00:00"
}
