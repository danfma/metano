using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(GDer), "g")]
public abstract record GBase<T>(T Value);
