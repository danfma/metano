using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "k", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(IgnAbsDer), "d")]
public abstract record IgnAbs(int V);
