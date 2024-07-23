using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoWrapper.Configurations;

public static class JsonSettingsConfiguration
{
    public static JsonSerializerOptions GetJsonSerializerOptions(
        JsonNamingPolicy jsonNamingPolicy,
        JsonIgnoreCondition defaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)
        => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = jsonNamingPolicy,
            WriteIndented = true,
            DefaultIgnoreCondition = defaultIgnoreCondition,
        };
}
