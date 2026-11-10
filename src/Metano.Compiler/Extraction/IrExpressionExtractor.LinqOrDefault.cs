using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The value an <c>OrDefault</c> LINQ terminal returns when no element
/// qualifies (#601). C# returns <c>default(TSource)</c>, or the
/// <c>defaultValue</c> argument of the .NET 6 overloads. <c>null</c> is that
/// value for reference and <c>Nullable&lt;T&gt;</c> elements only, so a stage
/// whose value is anything else lists it as an argument.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string DefaultValueParameterName = "defaultValue";

    /// <summary>
    /// The arguments of an <c>OrDefault</c> stage: <c>(predicate)</c> when it
    /// returns <c>null</c> on a miss, <c>(predicate, defaultValue)</c>
    /// otherwise, the predicate slot holding the omitted-argument marker when
    /// the call has none. A <c>default</c> literal written as the default
    /// value is <c>default(TSource)</c>, like no argument at all.
    /// </summary>
    // TODO(#597): a call that names `defaultValue:` before `predicate:` evaluates
    // them predicate-first here; reconcile with the evaluation order of #597.
    private IReadOnlyList<IrExpression> OrDefaultArguments(
        InvocationExpressionSyntax stage,
        IMethodSymbol method,
        IReadOnlyList<IrExpression> arguments
    )
    {
        var defaultValueIndex = stage.ArgumentList.Arguments.IndexOf(argument =>
            BoundParameterOf(argument, method).Name == DefaultValueParameterName
        );

        var predicate = PredicateOf(arguments, defaultValueIndex);
        var elementType = method.TypeArguments[0];

        var missValue =
            ExplicitDefaultValue(arguments, defaultValueIndex)
            ?? ElementMissValue.Of(elementType, BuildOrigin, MapElementType);

        if (missValue is null && LowersLinqChains)
            ElementMissValue.ReportUnrepresentable(stage, method, elementType);

        if (missValue is null or IrLiteral { Kind: IrLiteralKind.Null })
            return predicate is null ? [] : [predicate];

        return [predicate ?? OmittedArgument(), missValue];
    }

    // TODO(#549): the Dart target does not lower LINQ chains, so a warning
    // about the lowered terminal would describe code it never prints.
    private bool LowersLinqChains => _target is not TargetLanguage.Dart;

    private static IrExpression? PredicateOf(
        IReadOnlyList<IrExpression> arguments,
        int defaultValueIndex
    ) => arguments.Where((_, index) => index != defaultValueIndex).FirstOrDefault();

    /// <summary>
    /// The <c>defaultValue</c> argument, unless it is a <c>default</c>
    /// literal: that one keeps the opaque marker of an omitted argument.
    /// </summary>
    private static IrExpression? ExplicitDefaultValue(
        IReadOnlyList<IrExpression> arguments,
        int defaultValueIndex
    ) =>
        defaultValueIndex >= 0
        && arguments[defaultValueIndex] is not IrLiteral { Kind: IrLiteralKind.Default }
            ? arguments[defaultValueIndex]
            : null;

    private IrTypeRef MapElementType(ITypeSymbol elementType) =>
        IrTypeRefMapper.Map(elementType, _originResolver, _target);
}
