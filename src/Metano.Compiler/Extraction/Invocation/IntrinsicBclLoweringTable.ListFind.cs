using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Gives <c>List&lt;T&gt;.Find</c> (and <c>ImmutableList&lt;T&gt;</c>'s) an explicit miss value (#601): C# returns
/// <c>default(T)</c> when no element matches, which is <c>null</c> for
/// reference and <c>Nullable&lt;T&gt;</c> elements only.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>The lists whose <c>Find</c> / <c>FindLast</c> return <c>default(T)</c> on a miss.</summary>
    private static readonly string[] FindOwners =
    [
        "System.Collections.Generic.List`1",
        "System.Collections.Immutable.ImmutableList`1",
    ];

    /// <summary>
    /// <c>list.Find(match)</c> → <c>list.Find(match, default(T))</c>, and
    /// <c>FindLast</c> likewise (spec 011 contracts/runtime-collections.md). A
    /// struct without a target-agnostic default keeps <c>null</c> and is
    /// reported.
    /// </summary>
    private void SeedListFind(Compilation compilation)
    {
        foreach (var owner in FindOwners)
        {
            var list = compilation.GetTypeByMetadataName(owner);

            foreach (var name in (string[])[nameof(List<>.Find), nameof(List<>.FindLast)])
            {
                var find = list?.GetMembers(name).OfType<IMethodSymbol>().SingleOrDefault();

                if (find is not null)
                    _entries[find] = BuildListFindCall;
            }
        }
    }

    private IrExpression? BuildListFindCall(InvocationRewriteContext ctx)
    {
        // TODO(#549): Dart has no mapping for the call, so it keeps its source shape there.
        if (_helpers.Target is TargetLanguage.Dart)
            return null;

        var find = ctx.Symbol!;
        var elementType = find.ContainingType.TypeArguments[0];

        var missValue = DefaultValuePolicy.Of(
            elementType,
            _helpers.BuildOrigin,
            _helpers.MapTypeRef
        );

        if (missValue is null)
            DefaultValuePolicy.ReportUnrepresentable(ctx.Invocation, find, elementType);

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
