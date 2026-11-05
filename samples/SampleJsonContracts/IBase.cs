using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(IA), 1)]
[JsonDerivedType(typeof(IS), "s")]
public abstract record IBase;
