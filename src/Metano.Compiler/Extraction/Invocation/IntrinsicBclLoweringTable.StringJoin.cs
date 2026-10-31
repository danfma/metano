using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Gathers the values of a <c>string.Join</c> call into a single array
/// argument, whatever overload (<c>params</c> elements, an array, any
/// enumerable) the source picked.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// <c>string.Join(separator, values)</c> with the values gathered into a
    /// single array argument: expanded <c>params</c> elements become an
    /// array literal, an argument that already lowers to an array passes
    /// through, and any other enumerable is spread into an array literal.
    /// The <c>(separator, value, startIndex, count)</c> overloads are left
    /// alone.
    /// </summary>
    private void SeedStringJoin(Compilation compilation)
    {
        var stringType = compilation.GetSpecialType(SpecialType.System_String);

        foreach (
            var method in stringType
                .GetMembers("Join")
                .OfType<IMethodSymbol>()
                .Where(m => m.IsStatic && m.Parameters.Length == 2)
        )
        {
            _entries[method.OriginalDefinition] = BuildStringJoinCall;
        }
    }

    private IrExpression BuildStringJoinCall(InvocationRewriteContext ctx)
    {
        var arguments = ArgumentsInParameterOrder(BoundInvocation(ctx));
        var separator = ctx.Recurse((ExpressionSyntax)arguments[0].Value.Syntax);
        var values = JoinValuesArray(ctx, arguments[1]);

        return new IrCallExpression(
            new IrMemberAccess(new IrTypeReference("String"), ctx.Symbol!.Name),
            [new IrArgument(separator), new IrArgument(values)],
            Origin: _helpers.BuildOrigin(ctx.Symbol)
        );
    }

    private IrExpression JoinValuesArray(InvocationRewriteContext ctx, IArgumentOperation values)
    {
        // Expanded `params` elements: every source argument after the
        // separator, since `params` elements are always positional and last.
        if (values.ArgumentKind != ArgumentKind.Explicit)
        {
            return new IrArrayLiteral(
                ctx.Invocation.ArgumentList.Arguments.Skip(1)
                    .Select(a => ctx.Recurse(a.Expression))
                    .ToList()
            );
        }

        var value = ctx.Recurse((ExpressionSyntax)values.Value.Syntax);
        var valueType = UnconvertedType(values.Value);

        return valueType is not null && LowersToArray(valueType)
            ? value
            : new IrArrayLiteral([new IrSpreadExpression(value)]);
    }

    private bool LowersToArray(ITypeSymbol type) =>
        _helpers.MapTypeRef(type) switch
        {
            IrArrayTypeRef => true,
            IrNullableTypeRef { Inner: IrArrayTypeRef } => true,
            _ => false,
        };
}
