using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript policy for an explicit reference conversion (#669), decided
/// from the target-neutral <see cref="IrReferenceConversion"/> facts. The
/// result is typed unless the operand is already narrowed; it is checked with
/// <c>castTo</c> only where the module may import the target's class as a
/// value, since an ES module that imports a class whose base chain reaches back
/// to it fails when the modules load (<c>class Leaf extends Node</c> runs while
/// <c>Node</c> is still in its temporal dead zone):
/// <list type="bullet">
///   <item>not inside a type the target derives from (the target's module
///   imports this one to extend it);</item>
///   <item>not inside an <c>[Inline]</c> expansion, whose caller's module the
///   bridge cannot see;</item>
///   <item>not a class the running group asserts
///   (<see cref="GroupCasts.Asserted"/>): the transformer's import-graph pass
///   names the classes whose value import would close a cycle through a
///   third module.</item>
/// </list>
/// Every other typed cast is a TypeScript assertion (<c>value as T</c>).
/// </summary>
internal static class ReferenceCastLowering
{
    /// <summary>The <c>metano-runtime</c> helper that checks a cast.</summary>
    public const string CastHelper = "castTo";

    /// <summary>
    /// Whether the cast types its result; a cast an earlier type test narrows,
    /// and one to a type parameter of an <c>[Inline]</c> callee (out of scope
    /// in the caller), keep the bare operand.
    /// </summary>
    public static bool TypesResult(IrReferenceConversion conversion) =>
        !conversion.IsNarrowedByTypeTest
        && !(conversion.InInlineExpansion && conversion.TargetMentionsTypeParameter);

    /// <summary>Whether the module that holds the cast may import the target's class.</summary>
    public static bool MayImportTargetClass(IrReferenceConversion conversion) =>
        !conversion.InInlineExpansion && !conversion.TargetDerivesFromEnclosingType;

    /// <summary>
    /// Whether the cast checks against <paramref name="runtimeClass"/>: any
    /// class but one the running group asserts. A checked local class is
    /// recorded in <paramref name="casts"/> for the import-graph pass;
    /// without a group (<c>null</c>) every class is checked.
    /// </summary>
    public static bool Checks(TsExpression runtimeClass, GroupCasts? casts)
    {
        if (runtimeClass is not TsIdentifier { Name: var name } || casts is null)
            return true;

        if (casts.Asserted.Contains(name))
            return false;

        casts.Checked.Add(name);

        return true;
    }
}
