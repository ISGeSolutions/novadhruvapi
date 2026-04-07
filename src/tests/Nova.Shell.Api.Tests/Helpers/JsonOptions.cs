using System.Text.Json;

namespace Nova.Shell.Api.Tests.Helpers;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for deserialising Nova API responses.
/// Nova uses snake_case on the wire — always use these options with ReadFromJsonAsync&lt;T&gt;.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
