using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Gives every <c>x.GetHashCode()</c> call — whichever override it binds to
/// (<c>int</c>, <c>string</c>, an enum, a record, a user class) — the origin
/// of <c>object.GetHashCode</c>, so a backend needs a single lowering for it
/// (for example, TypeScript's declarative mapping to the runtime
/// <c>valueHashCode</c>, Dart's <c>hashCode</c> getter).
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    private void SeedHashCode(Compilation compilation)
    {
        var getHashCode = compilation
            .GetSpecialType(SpecialType.System_Object)
            .GetMembers(nameof(object.GetHashCode))
            .OfType<IMethodSymbol>()
            .Single(m => m.Parameters.IsEmpty);

        _entries[getHashCode] = BuildHashCodeCall;
    }

    /// <summary>
    /// Any receiver gets <c>object.GetHashCode</c>'s origin — an implicit
    /// receiver is <c>this</c>. The one exception is a <c>base.GetHashCode()</c>
    /// bound to an override the base declares itself: it must call that
    /// override, so the entry declines and the call keeps its own origin. A
    /// <c>base.GetHashCode()</c> bound to <c>object</c>'s or <c>ValueType</c>'s
    /// identity hash keeps the <c>base</c> receiver with <c>object</c>'s
    /// origin, for each backend to lower.
    /// </summary>
    private IrExpression? BuildHashCodeCall(InvocationRewriteContext ctx)
    {
        var symbol = ctx.Symbol!;
        var receiverSyntax = QualifiedMemberSyntax.From(ctx.Invocation.Expression)?.Receiver;

        if (receiverSyntax is BaseExpressionSyntax && !IsIdentityHash(symbol))
            return null;

        if (SymbolHelper.HasEmit(symbol) || SymbolHelper.GetImport(symbol) is not null)
            return null;

        var objectGetHashCode = RootDefinition(symbol);

        var receiver = receiverSyntax is null
            ? new IrThisExpression()
            : ctx.Recurse(receiverSyntax);

        return new IrCallExpression(
            new IrMemberAccess(receiver, objectGetHashCode.Name),
            [],
            Origin: _helpers.BuildOrigin(objectGetHashCode)
        );
    }

    private static bool IsIdentityHash(IMethodSymbol getHashCode) =>
        getHashCode.ContainingType.SpecialType
            is SpecialType.System_Object
                or SpecialType.System_ValueType;
}
