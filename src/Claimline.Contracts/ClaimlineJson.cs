using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claimline.Contracts;

/// <summary>One serializer config for every message and API payload, so enums travel as readable strings.</summary>
public static class ClaimlineJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
