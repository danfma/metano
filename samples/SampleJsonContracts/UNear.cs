using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor
)]
[JsonDerivedType(typeof(UNearA), "a")]
[JsonDerivedType(typeof(UNearAbs), "abs")]
public abstract record UNear(int A);
