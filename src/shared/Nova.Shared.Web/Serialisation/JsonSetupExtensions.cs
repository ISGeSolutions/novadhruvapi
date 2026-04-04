using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Nova.Shared.Web.Serialisation;

/// <summary>
/// Extension methods for configuring JSON serialisation on the Nova API stack.
/// </summary>
public static class JsonSetupExtensions
{
    /// <summary>
    /// Configures JSON serialisation for all Minimal API request binding and responses:
    /// <list type="bullet">
    ///   <item>Snake_case wire keys (e.g. <c>tenant_id</c>, <c>correlation_id</c>).</item>
    ///   <item>Case-insensitive property binding — camelCase or snake_case inputs both work.</item>
    ///   <item>
    ///     <see cref="DateTimeOffset"/> serialised as UTC with <c>Z</c> suffix:
    ///     <c>"yyyy-MM-ddTHH:mm:ssZ"</c> (e.g. <c>"2026-04-03T10:00:00Z"</c>).
    ///     UX treats this as a shifting timestamp — displayed in browser locale, sent back as UTC.
    ///   </item>
    ///   <item>
    ///     <see cref="DateOnly"/> serialised as <c>"yyyy-MM-dd"</c> (e.g. <c>"2026-08-15"</c>).
    ///     UX treats this as a fixed calendar date — never shifted by browser locale.
    ///   </item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddNovaJsonOptions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.DictionaryKeyPolicy         = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;

            // DateTimeOffset → "yyyy-MM-ddTHH:mm:ssZ" (always UTC, Z suffix, no fractional seconds)
            // DateOnly       → "yyyy-MM-dd"            (automatic, no converter needed)
            options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
        });

        return services;
    }
}
