using Metano.Annotations;

namespace SampleRuntimeTypes.Shapes;

/// <summary>An interface tested nominally (spec 011 D29, US3).</summary>
public interface IHasArea
{
    double Area { get; }
}

public abstract class Shape { }

public sealed class Circle : Shape, IHasArea
{
    public double Radius { get; init; } = 1;

    public double Area => 3 * Radius * Radius;
}

public sealed class Square : Shape
{
    public double Side { get; init; } = 1;
}

/// <summary>
/// A plain-object implementer: a value of its shape answers <c>is IHasArea</c>
/// structurally, through the interface's <c>plainImplementers</c> (FR-019).
/// </summary>
[PlainObject]
public sealed record AreaDto(double Area, string Unit) : IHasArea;

/// <summary>
/// An interface nothing asks about, whose descriptor <c>[EmitRuntimeType]</c>
/// forces anyway (FR-027). Internal, so the library rule (D29: a library's
/// public types are all reached) is not what reaches it.
/// </summary>
[Transpile, EmitRuntimeType]
internal interface IUnasked
{
    string Note { get; }
}

/// <summary>A class nothing asks about: internal, so it carries no descriptor (D29).</summary>
[Transpile]
internal sealed class Blob
{
    public int Size => 1;
}
