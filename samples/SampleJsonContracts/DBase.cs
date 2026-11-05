using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(DBer), "d")]
public record DBase;
