using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(ODer), "o")]
public abstract class OBase
{
    public int Base { get; init; }

    public abstract string Name { get; init; }
}
