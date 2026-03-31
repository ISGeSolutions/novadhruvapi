namespace Nova.Shell.Api.Endpoints;

/// <summary>Maps the <c>GET /hello-world</c> diagnostic endpoint.</summary>
public static class HelloWorldEndpoint
{
    /// <summary>Registers the endpoint on the given <see cref="WebApplication"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/hello-world", (IHttpContextAccessor httpContextAccessor) =>
        {
            string correlationId = httpContextAccessor.HttpContext?.Items["X-Correlation-ID"] as string
                                   ?? string.Empty;

            return Results.Ok(new
            {
                message = "Hello, World!",
                timestamp = DateTime.UtcNow,
                correlationId
            });
        })
        .AllowAnonymous()
        .WithName("HelloWorld");
    }
}
