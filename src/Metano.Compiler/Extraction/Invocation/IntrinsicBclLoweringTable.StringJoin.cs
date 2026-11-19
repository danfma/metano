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
                .Where(DefaultTextSites.IsJoin)
        )
        {
            _entries[method.OriginalDefinition] = BuildStringJoinCall;
        }
    }

    private IrExpression BuildStringJoinCall(InvocationRewriteContext ctx)
    {
        var arguments = ArgumentsInParameterOrder(BoundInvocation(ctx));
        var separator = ctx.Recurse((ExpressionSyntax)arguments[0].Value.Syntax);
        var values = WithDefaultText(ctx, JoinValuesArray(ctx, arguments[1]));

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
                    .Select(a => ExpandedElement(ctx, a.Expression))
                    .ToList()
            );
        }

        var value = ctx.Recurse((ExpressionSyntax)values.Value.Syntax);
        var valueType = UnconvertedType(values.Value);

        return valueType is not null && LowersToArray(valueType)
            ? value
            : new IrArrayLiteral([new IrSpreadExpression(value)]);
    }

    /// <summary>
    /// An expanded <c>params object[]</c> element: the argument converted as its static type
    /// prints (a <c>bool</c> argument boxes to <c>"True"</c>, not <c>"true"</c>).
    /// </summary>
    private IrExpression ExpandedElement(InvocationRewriteContext ctx, ExpressionSyntax element)
    {
        var value = ctx.Recurse(element);
        var type = ctx.SemanticModel.GetTypeInfo(element).Type;

        return
            DefaultTextConversion.MappedToString(
                type is ITypeParameterSymbol ? ctx.SemanticModel.Compilation.ObjectType : type,
                new(_helpers.Target, _lowersByDeclarativeMapping, DefaultTextSite.Implicit)
            )
                is { } toString
            ? JoinTextCall(value, toString, type)
            : value;
    }

    /// <summary>
    /// <c>values.map(item =&gt; item.ToString())</c> for a <c>Join&lt;T&gt;</c> over
    /// <c>bool</c>, <c>double</c> or <c>float</c> values when the target maps their
    /// <c>ToString()</c> (<see cref="DefaultTextConversion"/>, #308): the target's own join
    /// would convert each element its own way (<c>true</c> for <c>True</c>). Any other
    /// <c>Join</c> keeps the array as it is.
    /// </summary>
    private IrExpression WithDefaultText(InvocationRewriteContext ctx, IrExpression values)
    {
        var element = ctx.Symbol is { IsGenericMethod: true, TypeArguments: [var argument] }
            ? argument
            : (ctx.Symbol?.Parameters.LastOrDefault()?.Type as IArrayTypeSymbol)?.ElementType;

        if (element is null)
            return values;

        if (
            DefaultTextConversion.MappedToString(
                element is ITypeParameterSymbol
                    ? ctx.SemanticModel.Compilation.ObjectType
                    : element,
                new(_helpers.Target, _lowersByDeclarativeMapping, DefaultTextSite.Implicit)
            )
            is not { } toString
        )
            return values;

        const string ItemName = "item";

        var convert = new IrLambdaExpression(
            [new IrParameter(ItemName, _helpers.MapTypeRef(element), HasExplicitType: false)],
            ReturnType: null,
            [new IrReturnStatement(JoinTextCall(new IrIdentifier(ItemName), toString, element))]
        );

        return new IrCallExpression(new IrMemberAccess(values, "map"), [new IrArgument(convert)]);
    }

    private IrCallExpression JoinTextCall(
        IrExpression value,
        IMethodSymbol toString,
        ITypeSymbol? type
    ) =>
        DefaultTextConversion.Call(value, toString, _helpers.BuildOrigin(toString)) with
        {
            TypeArguments = type?.UnwrapNullableValueType() is { TypeKind: TypeKind.Enum } enumType
                ? [_helpers.MapTypeRef(enumType)]
                : null,
        };

    /// <summary>
    /// Whether a value of <paramref name="type"/> is a native array: for a
    /// target that keeps native collections the list-shaped collections are
    /// too; for the others they are runtime classes, which spread into one.
    /// </summary>
    private bool LowersToArray(ITypeSymbol type)
    {
        var mapped = _helpers.MapTypeRef(type);

        if (IrExtractionContext.KeepsNativeCollectionsFor(_helpers.Target))
            return mapped.AsLegacyArray() is not null;

        return (mapped is IrNullableTypeRef nullable ? nullable.Inner : mapped) is IrArrayTypeRef;
    }
}
