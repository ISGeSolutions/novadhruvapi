using Asp.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Nova.Shared.Web.Versioning;

/// <summary>
/// Extension methods for configuring API versioning on Nova services.
/// Uses URL segment versioning: <c>/api/v{n}/resource</c>.
/// </summary>
/// <remarks>
/// Usage in Program.cs:
/// <code>
/// // 1. Register services (before builder.Build())
/// builder.Services.AddNovaApiVersioning();
///
/// // 2. Create the version set and route group (after builder.Build())
/// var versionSet = app.NewApiVersionSet("Nova")
///     .HasApiVersion(new ApiVersion(1, 0))
///     .ReportApiVersions()
///     .Build();
///
/// RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
///     .WithApiVersionSet(versionSet)
///     .MapToApiVersion(new ApiVersion(1, 0));
///
/// // 3. Register versioned endpoints on the group — routes are relative (/hello-world, /echo)
/// HelloWorldEndpoint.Map(v1);
/// MyEndpoint.Map(v1);
/// </code>
///
/// Endpoint Map() methods accept <see cref="RouteGroupBuilder"/> and use relative routes:
/// <code>
/// public static void Map(RouteGroupBuilder group)
/// {
///     group.MapPost("/bookings/search", Handle)
///          .RequireAuthorization()
///          .WithName("SearchBookings");
/// }
/// </code>
///
/// Health checks and diagnostic endpoints register directly on <c>app</c> (not the group)
/// and are not subject to version validation.
/// </remarks>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Registers Asp.Versioning services configured for URL segment versioning.
    /// </summary>
    /// <remarks>
    /// Configuration:
    /// <list type="bullet">
    /// <item><description><c>DefaultApiVersion = 1.0</c></description></item>
    /// <item><description><c>AssumeDefaultVersionWhenUnspecified = false</c> — callers must include the version segment.</description></item>
    /// <item><description><c>ReportApiVersions = true</c> — adds <c>api-supported-versions</c> header to all responses.</description></item>
    /// <item><description><c>UrlSegmentApiVersionReader</c> — reads version from <c>{version:apiVersion}</c> route token.</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddNovaApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion                   = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = false;
            options.ReportApiVersions                   = true;
            options.ApiVersionReader                    = new UrlSegmentApiVersionReader();
        });

        return services;
    }
}
