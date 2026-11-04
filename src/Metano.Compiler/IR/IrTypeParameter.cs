namespace Metano.Compiler.IR;

/// <summary>
/// A type parameter declaration on a generic type or method
/// (e.g., <c>T</c> in <c>class Foo&lt;T&gt; where T : IComparable, IFormattable</c>).
/// </summary>
/// <param name="Name">The type parameter name (e.g., <c>T</c>, <c>TKey</c>).</param>
/// <param name="Constraints">Constraint types (e.g., <c>where T : IFoo, IBar</c>).
/// C# allows multiple constraints per type parameter.</param>
/// <param name="KindConstraint">The <c>class</c> or <c>struct</c> constraint
/// (<c>unmanaged</c> implies <c>struct</c>), which <see cref="Constraints"/>
/// cannot carry because it names no type.</param>
public sealed record IrTypeParameter(
    string Name,
    IReadOnlyList<IrTypeRef>? Constraints = null,
    IrTypeKindConstraint KindConstraint = IrTypeKindConstraint.Any
);

/// <summary>The kind of type a type argument must be.</summary>
public enum IrTypeKindConstraint
{
    /// <summary>No <c>class</c> or <c>struct</c> constraint.</summary>
    Any,

    /// <summary><c>where T : class</c>: a reference type.</summary>
    ReferenceType,

    /// <summary><c>where T : struct</c> or <c>unmanaged</c>: a value type.</summary>
    ValueType,
}
