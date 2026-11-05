using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(NMid), "mid")]
public abstract record NBase(int A);
