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
    bool RendersAsJsxElement = false
);
