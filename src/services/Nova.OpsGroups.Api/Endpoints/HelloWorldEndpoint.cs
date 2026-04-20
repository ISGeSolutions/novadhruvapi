namespace Nova.OpsGroups.Api.Endpoints;

public static class HelloWorldEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/grouptour-task-hello", () =>
                TypedResults.Ok(new { message = "Hello from Nova.OpsGroups.Api", version = "1.0" }))
             .WithName("GrouptourTaskHello")
             .AllowAnonymous();
}
