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
        // TODO(#549): Dart has no mapping for the call, so it keeps its source shape there.
        if (_helpers.Target is TargetLanguage.Dart)
            return null;

        var find = ctx.Symbol!;
        var elementType = find.ContainingType.TypeArguments[0];
        var missValue = ElementMissValue.Of(elementType, _helpers.BuildOrigin, _helpers.MapTypeRef);

        if (missValue is null)
            ElementMissValue.ReportUnrepresentable(ctx.Invocation, find, elementType);

        return new IrCallExpression(
            new IrMemberAccess(ReceiverOf(ctx), find.Name),
            [
                .. ArgumentValues(ctx, BoundInvocation(ctx)).Select(a => new IrArgument(a)),
                new(missValue ?? new IrLiteral(null, IrLiteralKind.Null)),
            ],
            Origin: _helpers.BuildOrigin(find)
        );
    }
}
