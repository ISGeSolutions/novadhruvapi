using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Status options: <c>POST /api/v1/user-profile/status-options</c>
///
/// Returns the tenant/company/branch-scoped list of presence status options
/// from <c>presets.user_status_options</c>. Most specific tier wins when the
/// same <c>status_code</c> appears at multiple scoping levels.
/// Returns an empty list if the tenant has no rows configured.
/// </summary>
public static class StatusOptionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/user-profile/status-options", HandleAsync)
             .RequireAuthorization()
             .WithName("StatusOptions");
    }

    private static async Task<IResult> HandleAsync(
        StatusOptionsRequest         request,
        IDbConnectionFactory         connectionFactory,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        IEnumerable<StatusOptionRow> options;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            options = await conn.QueryAsync<StatusOptionRow>(
                PresetsDbHelper.StatusOptionsQuerySql(presetsDb),
                new { request.TenantId, request.CompanyCode, request.BranchCode },
                commandTimeout: 10);
        }

        return TypedResults.Ok(options.Select(o => new
        {
            status_code = o.StatusCode,
            label       = o.Label,
            colour      = o.Colour,
        }));
    }

    private sealed record StatusOptionsRequest : RequestContext;
    private sealed record StatusOptionRow(string StatusCode, string Label, string Colour);
}
