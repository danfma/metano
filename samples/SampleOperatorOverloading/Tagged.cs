namespace SampleOperatorOverloading;

/// <summary>
/// A constructor overload typed by the class's type parameter: it takes any
/// value, including a primitive, while a concrete overload of the same arity
/// is tried first.
/// </summary>
public class Tagged<T>
{
    public Tagged(T value)
    {
        Kind = "value";
    }

    public Tagged(int code)
    {
        Kind = "code";
    }

    public Tagged(T value, string label)
    {
        Kind = $"value:{label}";
    }

    public string Kind { get; }
}
