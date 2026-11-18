using Metano.Annotations;

namespace SampleRuntimeTypes.Interop;

/// <summary>A wire shape: a plain object with no class identity.</summary>
[PlainObject]
public sealed record PointDto(int X, int Y);
