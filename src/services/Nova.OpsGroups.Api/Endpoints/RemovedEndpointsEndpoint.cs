namespace Nova.OpsGroups.Api.Endpoints;

/// <summary>
/// 410 Gone stubs for endpoints that have been removed or relocated.
/// </summary>
public static class RemovedEndpointsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-team-members",       Gone("Moved to Nova.Presets.Api: POST /api/v1/users/by-role"))
             .WithName("RemovedTeamMembers");

        group.MapPost("/grouptour-task-tour-generics",      Gone("Moved to Nova.Presets.Api: POST /api/v1/groups/tour-generics"))
             .WithName("RemovedTourGenerics");

        group.MapPost("/group-task-tour-generics-search",   Gone("Moved to Nova.Presets.Api: POST /api/v1/groups/tour-generics/search"))
             .WithName("RemovedTourGenericsSearch");

        group.MapPost("/grouptour-task-series",             Gone("Endpoint removed"))
             .WithName("RemovedSeries");

        group.MapPost("/grouptour-task-series-import",      Gone("Endpoint removed"))
             .WithName("RemovedSeriesImport");
    }

    private static Delegate Gone(string detail) =>
        () => TypedResults.Problem(
            title:      "Gone",
            detail:     detail,
            statusCode: StatusCodes.Status410Gone);
}
