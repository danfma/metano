using Metano.Annotations;

namespace SampleJsonContracts;

[PlainObject]
public sealed record Point3(double X, double Y, double Z) : Point(X);
