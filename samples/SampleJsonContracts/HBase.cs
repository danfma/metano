using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(HMid), "mid")]
[JsonDerivedType(typeof(HLeaf), "leaf")]
public abstract record HBase(int A);
