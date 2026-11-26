using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Bridges <c>IDisposable</c> to the JavaScript disposal protocol (FR-111). A class that
/// takes on <c>IDisposable</c> itself (<see cref="IrTypeSemantics.Disposable"/>) gains the
/// member a <c>using</c> declaration calls, which runs the member that implements
/// <c>Dispose()</c> under its emitted name (declared on the type or inherited):
/// <code>
/// [Symbol.dispose](): void {
///   disposeResource(this, this.dispose);
/// }
/// </code>
/// A <c>[Name("Close")]</c> implementation passes <c>this.close</c>. The call is virtual,
/// so a derived class that overrides <c>Dispose</c> is disposed through its override, and
/// a class whose base already takes on the contract inherits the member.
/// <para>
/// The runtime's <c>disposeResource</c> only invokes the method it is given; it is there
/// as the polyfill anchor. Its initializer installs <c>Symbol.dispose</c> on an engine
/// without one, and since the member reads the binding, every bundler that keeps the
/// class keeps that initializer and, following the ESM import graph (the runtime never
/// imports generated code), evaluates it before the class key. The well-known
/// <c>Symbol.dispose</c> key keeps an unused class removable (spec 011 FR-043).
/// </para>
/// </summary>
public static class IrToTsDisposalProtocolBridge
{
    private const string DisposeMemberLabel = "[Symbol.dispose]";
    private const string DisposeResourceHelper = "disposeResource";

    /// <summary>The disposal-protocol member <paramref name="ir"/> needs, if any.</summary>
    public static IEnumerable<TsMethodMember> BuildMembers(IrClassDeclaration ir)
    {
        if (ir.Semantics is { Disposable: { } disposable, IsPlainObject: false })
            yield return BuildDisposeMember(disposable.Dispose);
    }

    private static TsMethodMember BuildDisposeMember(IrContractMember dispose)
    {
        var self = new TsIdentifier("this");

        var method = new TsPropertyAccess(
            self,
            IrToTsNamingPolicy.ToMethodName(dispose.Name, dispose.Attributes)
        );

        var disposal = new TsCallExpression(
            new TsRuntimeHelperReference(DisposeResourceHelper),
            [self, method]
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
