namespace SampleRuntimeTypes.Shapes;

/// <summary>
/// The factory of US3, in its own class: a base whose module constructs its
/// subclasses would be an ESM import cycle.
/// </summary>
public static class ShapeFactory
{
    public static Shape Create(string kind) => kind == "circle" ? new Circle() : new Square();
}
