namespace Nova.OpsGroups.Api.Endpoints;

/// <summary>
/// 410 Gone stubs for endpoints that have been permanently removed.
/// </summary>
public static class RemovedEndpointsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-series",        Gone("Endpoint removed"))
             .WithName("RemovedSeries");

        group.MapPost("/grouptour-task-series-import", Gone("Endpoint removed"))
             .WithName("RemovedSeriesImport");
    }

    private static Delegate Gone(string detail) =>
        () => TypedResults.Problem(
            title:      "Gone",
            detail:     detail,
            statusCode: StatusCodes.Status410Gone);
}
