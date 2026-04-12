namespace Nova.CommonUX.Api.Endpoints;

/// <summary>Liveness check: <c>POST /api/v1/hello</c></summary>
public static class HelloWorldEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/hello", () => TypedResults.Ok(new { message = "Nova.CommonUX.Api is running." }))
             .AllowAnonymous()
             .WithName("HelloWorld");
    }
}
