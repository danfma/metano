namespace Metano.Compiler.IR;

/// <summary>
/// Describes what a type <em>is</em> semantically, without prescribing how any target
/// renders it. A backend reads these flags to decide which patterns to apply
/// (e.g., when <see cref="IsRecord"/> is true, TypeScript emits <c>equals</c> /
/// <c>hashCode</c> / <c>with</c> methods; Kotlin emits a <c>data class</c>).
/// </summary>
/// <param name="IsRecord">The type has value equality and <c>with</c>-expression support.</param>
/// <param name="IsValueType">Struct semantics — copied by value in the source language.</param>
/// <param name="IsStatic">Module-like type with no instantiation (C# <c>static class</c>).</param>
/// <param name="IsAbstract">Cannot be instantiated directly.</param>
/// <param name="IsSealed">Cannot be extended/inherited.</param>
/// <param name="IsPlainObject">Data shape only — no class wrapper needed (C# <c>[PlainObject]</c>).</param>
/// <param name="IsException">Extends the exception hierarchy.</param>
/// <param name="IsBranded">Single-field branded type (C# <c>[Branded]</c>, formerly <c>[InlineWrapper]</c>).</param>
/// <param name="BrandedUnderlyingType">The underlying primitive when <see cref="IsBranded"/> is true.</param>
/// <param name="IsJsTuple">Positional JS array-tuple shape (C# <c>[JsTuple]</c>) — lowered as an array tuple rather than a class.</param>
/// <param name="IsJsxComponent">Derives from a <c>[JsxComponentBuilder]</c> base — a backend that supports JSX emits it as a function component.</param>
/// <param name="JsxNativeElementTag">The tag from <c>[JsxNativeElement("tag")]</c> — a <c>new T { … }</c> of such a type lowers to an intrinsic <c>&lt;tag&gt;</c> element. Null when absent.</param>
/// <param name="RendersAsJsxElement">The type is JSX-renderable in a value position (a component, a native element, or an imported renderable typed as the marked element).</param>
/// <param name="Enumerable">The <c>IEnumerable</c> contract the type takes on itself — directly or through an interface — rather than inheriting it from its base type, with the member that implements <c>GetEnumerator()</c>. A backend whose iteration protocol differs from <c>GetEnumerator()</c> bridges it here. Null for every other type, and for interfaces, which declare the contract without implementing it.</param>
/// <param name="InheritsPerClassStorage">A class the toolchain emits (this compilation or a transpiled package) among the ancestors emits an auto-property with per-class storage (<see cref="IrPropertySemantics.EmitsPerClassStorage"/>).</param>
/// <param name="DeclaresToJson">The type, or a class the toolchain emits among its ancestors, declares an instance method the target emits as <c>toJSON</c>. A backend that synthesizes <c>toJSON</c> leaves it alone.</param>
/// <param name="Enumerator">The <c>IEnumerator</c> contract the type takes on itself, with the members that implement <c>Current</c> and <c>MoveNext()</c>, for a backend whose iterator protocol differs (JavaScript: <c>next()</c>). Null for every other type.</param>
public sealed record IrTypeSemantics(
    bool IsRecord = false,
    bool IsValueType = false,
    bool IsStatic = false,
    bool IsAbstract = false,
    bool IsSealed = false,
    bool IsPlainObject = false,
    bool IsException = false,
    bool IsBranded = false,
    IrTypeRef? BrandedUnderlyingType = null,
    bool IsJsTuple = false,
    bool IsJsxComponent = false,
    string? JsxNativeElementTag = null,
    bool RendersAsJsxElement = false,
    IrEnumerableContract? Enumerable = null,
    IrEnumeratorContract? Enumerator = null,
    bool InheritsPerClassStorage = false,
    bool DeclaresToJson = false
)
{
    /// <summary>
    /// The value equality C# gives the type, read from its symbols (spec 013
    /// FR-045); <see cref="IrValueEqualityShape.None"/> for a type with none.
    /// </summary>
    public IrValueEqualityShape ValueEquality { get; init; } = IrValueEqualityShape.None;

    /// <summary>
    /// The type is a C# 15 <c>closed</c> class: implicitly abstract
    /// (<see cref="IsAbstract"/> is also set), with every direct case known
    /// (<see cref="IrClassDeclaration.ClosedCases"/>).
    /// </summary>
    public bool IsClosed { get; init; }

    /// <summary>
    /// The class or struct takes on <c>IDisposable</c> itself — directly or through an
    /// interface — rather than inheriting it from its base type, for a backend whose
    /// disposal protocol differs from <c>Dispose()</c> (JavaScript: <c>[Symbol.dispose]()</c>).
    /// False for every other type, and for interfaces.
    /// </summary>
    public bool TakesOnDisposable { get; init; }
}
