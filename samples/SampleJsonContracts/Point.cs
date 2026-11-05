using Metano.Annotations;

namespace SampleJsonContracts;

[PlainObject]
public record Point(double X) : Shape;
