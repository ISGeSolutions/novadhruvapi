using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.Shared.Web.Serialisation;

/// <summary>
/// Serialises <see cref="DateTimeOffset"/> values as UTC with a literal <c>Z</c> suffix,
/// matching the Nova API wire contract: <c>"yyyy-MM-ddTHH:mm:ssZ"</c>.
/// Sub-second precision is intentionally dropped — API timestamps never expose fractional seconds.
/// </summary>
/// <remarks>
/// Registered globally in <see cref="JsonSetupExtensions.AddNovaJsonOptions"/> — do not apply
/// <c>[JsonConverter]</c> attributes to individual properties.
///
/// <para><b>Write:</b> always emits UTC with <c>Z</c> suffix, e.g. <c>"2026-04-03T10:00:00Z"</c>.
/// If the value is not already UTC its offset is converted before serialisation.</para>
///
/// <para><b>Read:</b> accepts <c>Z</c>, <c>+00:00</c>, or any explicit offset
/// (e.g. <c>+05:30</c>) and normalises to UTC.
/// UX is required to pre-shift to UTC before sending; this normalisation is a safety net only.</para>
/// </remarks>
internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc/>
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // TryGetDateTimeOffset handles Z, +00:00, +05:30, etc.
        if (reader.TryGetDateTimeOffset(out DateTimeOffset value))
            return value.ToUniversalTime();

        // Fallback for non-standard strings.
        string? raw = reader.GetString();
        return DateTimeOffset.TryParse(raw, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : default;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        // Normalise to UTC, drop sub-second precision, append literal Z.
        // "yyyy-MM-dd'T'HH:mm:ss" + "Z"  →  "2026-04-03T10:00:00Z"
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss") + "Z");
    }
}
