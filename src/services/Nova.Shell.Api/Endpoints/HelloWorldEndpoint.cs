namespace Nova.Shell.Api.Endpoints;

/// <summary>Maps the <c>GET /api/v1/hello-world</c> liveness endpoint.</summary>
public static class HelloWorldEndpoint
{
    /// <summary>Registers the endpoint on the versioned route group.</summary>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/hello-world", (IHttpContextAccessor httpContextAccessor) =>
        {
            string correlationId = httpContextAccessor.HttpContext?.Items["X-Correlation-ID"] as string
                                   ?? string.Empty;

            return TypedResults.Ok(new HelloWorldResponse(
                Message:      "Hello, World!",
                Timestamp:    DateTimeOffset.UtcNow,
                CorrelationId: correlationId,

                // DATE HANDLING REFERENCE — see planning/ainotes/nova-shell-api-guide.md § Date and Time Handling
                //
                // DepDate  → DateOnly  → wire: "dep_date": "2026-08-15"
                //   Represents a calendar date (departure, check-in, booking).
                //   Never includes time or offset. Never shifts with browser timezone.
                //   Stored in DB as DATE column. Frontend sends yyyy-MM-dd.
                //
                // CreatedOn → DateTimeOffset → wire: "created_on": "2026-04-03T10:00:00+00:00"
                //   Represents a UTC point in time (audit timestamp, event time).
                //   Always use DateTimeOffset.UtcNow for server-generated values.
                //   Stored in DB as DATETIME2 (MSSQL) / TIMESTAMPTZ (Postgres).
                //   Frontend sends yyyy-MM-ddThh:mm:ssZ; API normalises to UTC before storing.
                DepDate:   new DateOnly(2026, 8, 15),
                CreatedOn: DateTimeOffset.UtcNow));
        })
        .AllowAnonymous()
        .WithName("HelloWorld");
    }

    // Response record — properties serialise to snake_case on the wire:
    //   {
    //     "message":       "Hello, World!",
    //     "timestamp":     "2026-04-03T10:00:00Z",
    //     "correlation_id":"...",
    //     "dep_date":      "2026-08-15",     ← DateOnly        → yyyy-MM-dd, no time, no offset
    //     "created_on":    "2026-04-03T10:00:00Z"   ← DateTimeOffset → UTC, Z suffix
    //   }
    private sealed record HelloWorldResponse(
        string          Message,
        DateTimeOffset  Timestamp,
        string          CorrelationId,
        DateOnly        DepDate,
        DateTimeOffset  CreatedOn);
}
