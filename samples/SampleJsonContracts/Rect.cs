using Metano.Annotations;

namespace SampleJsonContracts;

[PlainObject]
public sealed record Rect(double W, double H) : Shape;
