using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The declarations that replace a type's synthesized value-equality members,
/// read from its <see cref="IrTypeSemantics.ValueEquality"/> (spec 013 FR-045):
/// C# uses a record's <c>Equals(R)</c>, a struct's <c>override Equals(object)</c> or
/// interface-implementing <c>Equals(S)</c>, and an <c>override GetHashCode()</c>
/// instead of synthesizing them. The one IR-side facade of those facts.
/// </summary>
public static class IrRecordEqualityOverrides
{
    /// <summary>The type declares the member C# uses instead of the synthesized <c>Equals(object)</c>.</summary>
    public static bool DeclaresEquals(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.ReplacesEquals;

    /// <summary>The type declares <c>override GetHashCode()</c>.</summary>
    public static bool DeclaresHashCode(IrClassDeclaration type) =>
        type.Semantics.ValueEquality.ReplacesHashCode;
}
