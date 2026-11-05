using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(UBaseA), "a")]
public abstract record UBaseH(int A);
