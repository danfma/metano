using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "k", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(IgnDer), "d")]
public record IgnBase(int V);
