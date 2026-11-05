using Metano.Annotations;

namespace SampleJsonContracts;

[PlainObject]
public sealed record MixedPoint(int X) : MixedBase;
