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
///   (<see cref="AssertingClasses"/>): the transformer's import-graph pass
///   names the classes whose value import would close a cycle through a
///   third module.</item>
/// </list>
/// Every other typed cast is a TypeScript assertion (<c>value as T</c>).
/// </summary>
internal static class ReferenceCastLowering
{
    /// <summary>The <c>metano-runtime</c> helper that checks a cast.</summary>
    public const string CastHelper = "castTo";

    private static readonly AsyncLocal<GroupCastScope?> _scope = new();

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
    /// recorded for the import-graph pass.
    /// </summary>
    public static bool Checks(TsExpression runtimeClass)
    {
        if (runtimeClass is not TsIdentifier { Name: var name } || _scope.Value is not { } scope)
            return true;

        if (scope.Asserted.Contains(name))
            return false;

        scope.Checked.Add(name);

        return true;
    }

    /// <summary>
    /// Opens the cast scope of one group's transform: casts to the classes
    /// named in <paramref name="asserted"/> are asserted, and every local
    /// class a cast checks is collected into the returned scope.
    /// </summary>
    public static GroupCastScope AssertingClasses(IReadOnlySet<string> asserted)
    {
        var scope = new GroupCastScope(asserted, _scope.Value);
        _scope.Value = scope;

        return scope;
    }

    /// <summary>
    /// The cast state of one group's transform. Disposing it restores the
    /// enclosing scope.
    /// </summary>
    internal sealed class GroupCastScope(IReadOnlySet<string> asserted, GroupCastScope? previous)
        : IDisposable
    {
        public IReadOnlySet<string> Asserted { get; } = asserted;

        /// <summary>The local classes the group's casts check with <c>castTo</c>, by emitted name.</summary>
        public HashSet<string> Checked { get; } = new(StringComparer.Ordinal);

        public void Dispose() => _scope.Value = previous;
    }
}
