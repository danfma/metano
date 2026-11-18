using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Which emitted classes carry the value equality C# gives them without a
/// declaration: a record (<c>equals</c>, <c>hashCode</c> and <c>with</c>) and a
/// plain struct, whose inherited <c>ValueType.Equals</c> compares every field
/// (<c>equals</c> and <c>hashCode</c>), so <c>EqualityComparer&lt;T&gt;.Default</c>
/// (the runtime <c>defaultEquals</c>) finds it behind an <c>object</c> or
/// interface, and which declarations replace those members: C# uses a record's
/// <c>Equals(R)</c>, a struct's <c>override Equals(object)</c> or
/// interface-implementing <c>Equals(S)</c>, and an <c>override GetHashCode()</c>
/// instead of synthesizing them. The one IR-side reader of the type's
/// <see cref="IrTypeSemantics.ValueEquality"/>, which the extractor reads from
/// the symbols (spec 013 FR-045).
/// </summary>
public static class IrValueEqualitySynthesis
{
    /// <summary>A record emitted as a class: <c>equals</c>, <c>hashCode</c> and <c>with</c>.</summary>
    public static bool SynthesizesRecordMembers(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.Kind is IrValueEqualityKind.Record;

    /// <summary>A plain (non-record) struct emitted as a class: <c>equals</c> and <c>hashCode</c>.</summary>
    public static bool SynthesizesStructEquality(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.Kind is IrValueEqualityKind.Struct;

    /// <summary>Whether <paramref name="type"/> receives a synthesized <c>equals</c> / <c>hashCode</c> pair.</summary>
    public static bool SynthesizesEquality(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.Kind is not IrValueEqualityKind.None;

    /// <summary>The type declares the member C# uses instead of the synthesized <c>Equals(object)</c>.</summary>
    public static bool DeclaresEquals(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.ReplacesEquals;

    /// <summary>The type declares <c>override GetHashCode()</c>.</summary>
    public static bool DeclaresHashCode(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.ReplacesHashCode;
}
