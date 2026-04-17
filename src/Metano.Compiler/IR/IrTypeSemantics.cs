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
/// <param name="IsInlineWrapper">Single-field branded type (C# <c>[InlineWrapper]</c>).</param>
/// <param name="InlineWrappedType">The underlying primitive when <see cref="IsInlineWrapper"/> is true.</param>
public sealed record IrTypeSemantics(
    bool IsRecord = false,
    bool IsValueType = false,
    bool IsStatic = false,
    bool IsAbstract = false,
    bool IsSealed = false,
    bool IsPlainObject = false,
    bool IsException = false,
    bool IsInlineWrapper = false,
    IrTypeRef? InlineWrappedType = null
);
