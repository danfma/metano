using System.Text.Json.Serialization;

namespace SampleJsonContracts;

public sealed class Account
{
    public string Name { get; set; } = "";

    [JsonInclude]
    internal int Version { get; set; }

    [JsonInclude]
    [JsonPropertyOrder(-1)]
    internal string Id = "";

    [JsonInclude]
    public int Hits;
}
