using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(MixedClass), "cls")]
[JsonDerivedType(typeof(MixedPoint), "pt")]
public abstract record MixedBase;
