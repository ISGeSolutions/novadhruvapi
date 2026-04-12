using System.Text.Json;

namespace Nova.Presets.Api.Tests.Helpers;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for deserialising Nova API responses in tests.
/// Nova uses snake_case on the wire — always use <see cref="Default"/> when calling
/// <c>ReadFromJsonAsync&lt;T&gt;</c>. Never deserialise with the default options.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
