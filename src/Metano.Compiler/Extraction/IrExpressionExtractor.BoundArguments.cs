using System.Collections.Immutable;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The arguments of a call lowered to the exact overload Roslyn resolved
/// (spec 013 FR-017, DECIDED-21): Roslyn's bound form, one value per
/// parameter in parameter order. An omitted optional parameter passes its
/// default explicitly, an expanded <c>params</c> call passes its elements and
/// a normal-form one spreads the array it passes. The overload is selected
/// statically, so the target never rebinds an omitted default by arity.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The bound arguments of <paramref name="call"/> — a <c>new</c> or a
    /// constructor initializer, explicit or implicit — or <c>null</c> when an
    /// argument has no syntax this extractor can lower (a <c>params</c>
    /// collection that is not an array), so the caller keeps the dispatcher
    /// call.
    /// </summary>
    internal List<IrArgument>? ExtractBoundArguments(IOperation? call)
    {
        var arguments = call switch
        {
            IObjectCreationOperation creation => creation.Arguments,
            IInvocationOperation invocation => invocation.Arguments,
            _ => ImmutableArray<IArgumentOperation>.Empty,
        };

        if (arguments.Any(argument => !IsBindable(argument)))
            return null;

        return arguments
            .OrderBy(argument => argument.Parameter!.Ordinal)
            .SelectMany(BoundArgumentsOf)
            .ToList();
    }

    private static bool IsBindable(IArgumentOperation argument) =>
        argument.Parameter is not null
        && argument.ArgumentKind switch
        {
            ArgumentKind.Explicit => argument.Syntax is ArgumentSyntax,
            ArgumentKind.ParamArray => argument.Value is IArrayCreationOperation,
            ArgumentKind.DefaultValue => true,
            _ => false,
        };

    private IEnumerable<IrArgument> BoundArgumentsOf(IArgumentOperation argument) =>
        argument.ArgumentKind switch
        {
            ArgumentKind.ParamArray => ExpandedParamsElementsOf(argument),
            ArgumentKind.DefaultValue => [new IrArgument(BoundDefaultOf(argument))],
            _ => [ExplicitBoundArgumentOf(argument)],
        };

    /// <summary>
    /// An argument the source passes, positional now: a <c>params</c> slot
    /// passed explicitly is the normal form, so the array spreads into the
    /// rest parameter.
    /// </summary>
    private IrArgument ExplicitBoundArgumentOf(IArgumentOperation argument)
    {
        var syntax = (ArgumentSyntax)argument.Syntax;
        var extracted = ExtractArgument(syntax) with { Name = null };
        var parameter = argument.Parameter!;

        if (!parameter.IsParams)
            return extracted;

        return NormalizeNullParamsArray(
            extracted with
            {
                IsSpread = true,
            },
            syntax.Expression,
            (IMethodSymbol)parameter.ContainingSymbol
        );
    }

    /// <summary>The elements an expanded <c>params</c> call passes, one argument each.</summary>
    private IEnumerable<IrArgument> ExpandedParamsElementsOf(IArgumentOperation argument)
    {
        var elements =
            ((IArrayCreationOperation)argument.Value).Initializer?.ElementValues
            ?? ImmutableArray<IOperation>.Empty;

        return elements.Select(element => new IrArgument(Extract(ElementSyntaxOf(element))));
    }

    private static ExpressionSyntax ElementSyntaxOf(IOperation element) =>
        element.Syntax switch
        {
            ArgumentSyntax argument => argument.Expression,
            ExpressionSyntax expression => expression,
            _ => throw new InvalidOperationException(
                $"A params element has unexpected syntax '{element.Syntax.Kind()}'."
            ),
        };

    /// <summary>
    /// The value Roslyn binds to an omitted optional parameter: its constant
    /// (a caller-info parameter's included). A <c>default</c> of a
    /// non-nullable struct has no literal here, so it passes the omitted
    /// marker and the callee's own default builds the zero value.
    /// </summary>
    private IrExpression BoundDefaultOf(IArgumentOperation argument)
    {
        var parameter = argument.Parameter!;
        var constant = argument.Value.ConstantValue;
        var value = constant.HasValue ? constant.Value : parameter.ExplicitDefaultValue;

        return value switch
        {
            null when IsNonNullableValueType(parameter.Type) => new IrLiteral(
                null,
                IrLiteralKind.Default
            ),
            decimal amount => new IrLiteral(amount, IrLiteralKind.Decimal),
            _ => BuildLiteralForDefault(value, parameter.Type),
        };
    }

    private static bool IsNonNullableValueType(ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
}
