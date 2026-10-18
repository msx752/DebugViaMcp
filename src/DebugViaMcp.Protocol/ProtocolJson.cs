using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace DebugViaMcp.Protocol;

/// <summary>
/// The single definition of the wire and discovery-file JSON conventions (spec §2.4):
/// camelCase properties, enums as camelCase strings, nulls omitted, ISO-8601 dates.
/// Any adapter written in another language must produce and accept exactly this shape.
/// </summary>
public static class ProtocolJson
{
    public static JsonSerializerSettings Settings { get; } = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
    };

    /// <summary>Applies the conventions to a serializer owned by someone else (e.g. StreamJsonRpc's JsonMessageFormatter).</summary>
    public static void Configure(JsonSerializer serializer)
    {
        if (serializer is null) throw new System.ArgumentNullException(nameof(serializer));

        serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
        serializer.NullValueHandling = Settings.NullValueHandling;
        serializer.DateParseHandling = Settings.DateParseHandling;
        foreach (var converter in Settings.Converters) serializer.Converters.Add(converter);
    }

    public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

    public static T? Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
}
