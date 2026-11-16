using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Which emitted classes carry the value equality C# gives them without a
/// declaration: a record (<c>equals</c>, <c>hashCode</c> and <c>with</c>) and a
/// plain struct, whose inherited <c>ValueType.Equals</c> compares every field
/// (<c>equals</c> and <c>hashCode</c>), so <c>EqualityComparer&lt;T&gt;.Default</c>
/// (the runtime <c>defaultEquals</c>) finds it behind an <c>object</c> or
/// interface. A type emitted without a class wrapper (<c>[PlainObject]</c>,
/// <c>[JsTuple]</c>, <c>[Branded]</c>, a JSX component) has none. Single
/// source of truth for the backend that synthesizes the members and the
/// runtime-requirement scan that imports their helpers.
/// </summary>
public static class IrValueEqualitySynthesis
{
    /// <summary>A record emitted as a class: <c>equals</c>, <c>hashCode</c> and <c>with</c>.</summary>
    public static bool SynthesizesRecordMembers(IrClassDeclaration type) =>
        type.Semantics.IsRecord && IsEmittedAsClass(type.Semantics);

    /// <summary>A plain (non-record) struct emitted as a class: <c>equals</c> and <c>hashCode</c>.</summary>
    public static bool SynthesizesStructEquality(IrClassDeclaration type) =>
        type.Semantics is { IsValueType: true, IsRecord: false }
        && IsEmittedAsClass(type.Semantics);

    /// <summary>Whether <paramref name="type"/> receives a synthesized <c>equals</c> / <c>hashCode</c> pair.</summary>
    public static bool SynthesizesEquality(IrClassDeclaration type) =>
        SynthesizesRecordMembers(type) || SynthesizesStructEquality(type);

    /// <summary>
    /// The type declares its own <c>Equals</c>, which C# uses instead of the
    /// synthesized one: a record's <c>Equals(R? other)</c>, or a struct's
    /// <c>Equals(object?)</c> override or <c>Equals(S)</c>.
    /// </summary>
    public static bool DeclaresEquals(IrClassDeclaration type) =>
        type.Semantics.IsRecord
            ? IrRecordEqualityOverrides.DeclaresEquals(type)
            : IrRecordEqualityOverrides.DeclaresAnyEquals(type);

    private static bool IsEmittedAsClass(IrTypeSemantics semantics) =>
        !semantics.IsPlainObject
        && !semantics.IsJsTuple
        && !semantics.IsBranded
        && !semantics.IsJsxComponent;
}
