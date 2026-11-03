using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// A direct <c>t.Equals(u)</c> over a tuple is rebuilt with the receiver's
/// static tuple type attached (<see cref="IrCallExpression.TupleType"/>), so
/// a backend whose tuples are positional arrays — where a nested tuple and a
/// C# array are indistinguishable — can compare nested tuple elements by
/// value (#331). The output for a flat tuple stays byte-identical to the
/// unmapped call.
/// <para>
/// Only the strongly-typed <c>Equals(ValueTuple&lt;…&gt;)</c> overload is
/// seeded: <c>Equals(object)</c> overrides root at <c>object.Equals</c>,
/// which this table's override-chain dispatch cannot reach without claiming
/// every <c>Equals</c> call in user code. An <c>Equals</c> with an
/// object-typed argument therefore keeps the plain mapping, whose nested
/// elements compare by reference.
/// </para>
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    private void SeedValueTupleEquals(Compilation compilation)
    {
        foreach (var arity in Enumerable.Range(1, SymbolHelper.MaxFlatTupleArity))
        {
            var metadataName = $"System.ValueTuple`{arity}";
            var valueTuple = compilation.GetTypeByMetadataName(metadataName);

            if (valueTuple is null)
                continue;

            foreach (
                var equals in valueTuple
                    .GetMembers(nameof(object.Equals))
                    .OfType<IMethodSymbol>()
                    .Where(m =>
                        m is { IsStatic: false, Parameters.Length: 1 }
                        && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, valueTuple)
                    )
            )
            {
                _entries[equals.OriginalDefinition] = RewriteTupleEquals;
            }
        }
    }

    /// <summary>
    /// Rebuilds the call with its tuple type, mirroring the generic
    /// invocation path argument-for-argument. Declines (returns
    /// <c>null</c>, falling back to that path) unless the receiver is
    /// statically a tuple and the single argument is positional.
    /// </summary>
    private IrExpression? RewriteTupleEquals(InvocationRewriteContext ctx)
    {
        var arguments = ctx.Invocation.ArgumentList.Arguments;

        if (arguments is not [{ NameColon: null }])
            return null;

        if (ctx.Invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        if (
            ctx.SemanticModel.GetTypeInfo(memberAccess.Expression).Type
            is not INamedTypeSymbol { IsTupleType: true } tupleType
        )
            return null;

        return new IrCallExpression(
            ctx.Recurse(ctx.Invocation.Expression),
            [ctx.ExtractArgument(arguments[0])],
            Origin: _helpers.BuildOrigin(ctx.Symbol),
            TupleType: _helpers.MapTypeRef(tupleType) as IrTupleTypeRef
        );
    }
}
