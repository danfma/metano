namespace Metano.Compiler.IR;

/// <summary>
/// A type parameter declaration on a generic type or method
/// (e.g., <c>T</c> in <c>class Foo&lt;T&gt; where T : IComparable, IFormattable</c>).
/// </summary>
/// <param name="Name">The type parameter name (e.g., <c>T</c>, <c>TKey</c>).</param>
/// <param name="Constraints">Constraint types (e.g., <c>where T : IFoo, IBar</c>).
/// C# allows multiple constraints per type parameter.</param>
public sealed record IrTypeParameter(string Name, IReadOnlyList<IrTypeRef>? Constraints = null);
