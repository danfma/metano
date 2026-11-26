using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Bridges <c>IDisposable</c> to the JavaScript disposal protocol (FR-111). A class that
/// takes on <c>IDisposable</c> itself (<see cref="IrTypeSemantics.TakesOnDisposable"/>)
/// gains the member a <c>using</c> declaration calls:
/// <code>
/// [Symbol.dispose](): void {
///   disposeResource(this);
/// }
/// </code>
/// The runtime's <c>disposeResource</c> calls <c>dispose()</c>, so a derived class that
/// overrides <c>Dispose</c> is disposed through its override, and a class whose base
/// already takes on the contract inherits the member. Reading the runtime binding is what
/// installs <c>Symbol.dispose</c> on an engine without one before the class is defined: a
/// bundle that keeps the class keeps its initializer, while the well-known key keeps an
/// unused class removable (spec 011 FR-043).
/// </summary>
public static class IrToTsDisposalProtocolBridge
{
    private const string DisposeMemberLabel = "[Symbol.dispose]";
    private const string DisposeResourceHelper = "disposeResource";

    /// <summary>The disposal-protocol member <paramref name="ir"/> needs, if any.</summary>
    public static IEnumerable<TsMethodMember> BuildMembers(IrClassDeclaration ir)
    {
        if (ir.Semantics is { TakesOnDisposable: true, IsPlainObject: false })
            yield return BuildDisposeMember();
    }

    private static TsMethodMember BuildDisposeMember()
    {
        var disposal = new TsCallExpression(
            new TsRuntimeHelperReference(DisposeResourceHelper),
            [new TsIdentifier("this")]
        );

        return new TsMethodMember(
            DisposeMemberLabel,
            [],
            new TsVoidType(),
            [new TsExpressionStatement(disposal)],
            ComputedKey: new TsPropertyAccess(new TsIdentifier("Symbol"), "dispose")
        );
    }
}
