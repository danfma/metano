using System.Text.Json.Serialization;

namespace SampleJsonContracts;

// NMid is itself polymorphic while registered on NBase (W17/W18, the nested
// polymorphism rows), which the transpiler reports as the informational MS0033
// warning; both behaviors are the measured System.Text.Json ones.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "u")]
[JsonDerivedType(typeof(NLeaf), "leaf")]
public record NMid(int A, int B) : NBase(A);
