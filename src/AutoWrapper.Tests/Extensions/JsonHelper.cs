using System.Text.Json;
using AutoWrapper.Configurations;

namespace AutoWrapper.Tests.Extensions;

public static class JsonHelper
{
    public static string ToJson<T>(
        this T value,
        JsonSerializerOptions? jsonOptions)
    {
        var autowrapperOptions = new AutoWrapperOptions();
        var defaultJsonOptions = JsonSettingsConfiguration.GetJsonSerializerOptions(
            autowrapperOptions.JsonNamingPolicy,
            autowrapperOptions.DefaultIgnoreCondition);
        return JsonSerializer.Serialize(value, jsonOptions ?? defaultJsonOptions);
    }
}
