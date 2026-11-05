using System.Text.Json.Serialization;
using Metano.Annotations;

namespace SampleJsonContracts;

[Branded]
public readonly record struct Title(string Value)
{
    [JsonFactory]
    public static Title From(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("blank");
        }

        return new Title(trimmed);
    }
}
