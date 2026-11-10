using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Gives <c>List&lt;T&gt;.Find</c> an explicit miss value (#601): C# returns
/// <c>default(T)</c> when no element matches, which is <c>null</c> for
/// reference and <c>Nullable&lt;T&gt;</c> elements only.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// <c>list.Find(match)</c> → <c>list.Find(match, default(T))</c>. A
    /// struct without a target-agnostic default keeps <c>null</c> and is
    /// reported.
    /// </summary>
    private void SeedListFind(Compilation compilation)
    {
        var find = compilation
            .GetTypeByMetadataName("System.Collections.Generic.List`1")
            ?.GetMembers(nameof(List<>.Find))
            .OfType<IMethodSymbol>()
            .SingleOrDefault();

        if (find is not null)
            _entries[find] = BuildListFindCall;
    }

    private IrExpression? BuildListFindCall(InvocationRewriteContext ctx)
    {
        // Dart has no mapping for the call, so it keeps its source shape there.
        if (_helpers.Target is TargetLanguage.Dart)
            return null;

        var symbol = ctx.Symbol!;
        var receiverSyntax = QualifiedMemberSyntax.From(ctx.Invocation.Expression)?.Receiver;

        var receiver = receiverSyntax is null
            ? new IrThisExpression()
            : ctx.Recurse(receiverSyntax);

        return new IrCallExpression(
            new IrMemberAccess(receiver, symbol.Name),
            [
                .. ArgumentValues(ctx, BoundInvocation(ctx)).Select(a => new IrArgument(a)),
                new(MissValue(ctx, symbol)),
            ],
            Origin: _helpers.BuildOrigin(symbol)
        );
    }

    private IrExpression MissValue(InvocationRewriteContext ctx, IMethodSymbol find)
    {
        var elementType = find.ContainingType.TypeArguments[0];
        var elementDefault = DefaultValueLiterals.For(elementType, _helpers.BuildOrigin);

        if (elementDefault is not IrLiteral { Kind: IrLiteralKind.Default })
            return elementDefault;

        UnrepresentableMissValue.Report(ctx.Invocation, find, elementType);

        return new IrLiteral(null, IrLiteralKind.Null);
    }
}
