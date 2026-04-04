namespace Nova.ToDo.Api.Endpoints;

/// <summary>Maps the <c>GET /api/v1/hello-world</c> liveness endpoint.</summary>
public static class HelloWorldEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/hello-world", (IHttpContextAccessor httpContextAccessor) =>
        {
            string correlationId = httpContextAccessor.HttpContext?.Items["X-Correlation-ID"] as string
                                   ?? string.Empty;

            return TypedResults.Ok(new HelloWorldResponse(
                Message:       "Hello from Nova.ToDo.Api!",
                Timestamp:     DateTimeOffset.UtcNow,
                CorrelationId: correlationId));
        })
        .AllowAnonymous()
        .WithName("HelloWorld");
    }

    private sealed record HelloWorldResponse(
        string         Message,
        DateTimeOffset Timestamp,
        string         CorrelationId);
}
