using System.Text.Json.Serialization;
using Metano.Annotations;

namespace SampleJsonContracts;

[PlainObject]
[JsonDerivedType(typeof(Point), "point")]
[JsonDerivedType(typeof(Point3), "point3")]
[JsonDerivedType(typeof(Rect), "rect")]
public abstract record Shape;
