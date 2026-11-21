namespace SampleRuntimeTypes.Shapes;

/// <summary>The runtime questions of US3, asked of a value whose static type is erased.</summary>
public static class ShapeQuestions
{
    public static string NameOf(string kind) => ShapeFactory.Create(kind).GetType().Name;

    public static bool IsCircle(string kind) =>
        ShapeFactory.Create(kind).GetType() == typeof(Circle);

    public static bool HasArea(object value) => value is IHasArea;

    public static double AreaOf(object value) => value is IHasArea area ? area.Area : -1;

    public static AreaDto ToAreaDto(object value) => (AreaDto)value;

    private static bool Is<T>(object value) => value is T;

    public static bool IsCircleViaGeneric(object value) => Is<Circle>(value);
}

/// <summary>Generic box for US4 reified class-slot runtime checks.</summary>
public sealed class TypedBox<T>
{
    public bool Matches(object value) => value is T;
}
