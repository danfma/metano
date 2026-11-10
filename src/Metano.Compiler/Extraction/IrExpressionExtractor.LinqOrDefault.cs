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
    /// The arguments of an <c>OrDefault</c> stage as
    /// <c>(predicate, defaultValue)</c>, the predicate slot holding the
    /// omitted-argument marker when the call has none. A stage that returns
    /// <c>null</c> on a miss keeps its source arguments.
    /// </summary>
    private IReadOnlyList<IrExpression> OrDefaultArguments(
        InvocationExpressionSyntax stage,
        IMethodSymbol method,
        IReadOnlyList<IrExpression> arguments
    )
    {
        var defaultValueIndex = stage.ArgumentList.Arguments.IndexOf(argument =>
            BoundParameterOf(argument, method).Name == DefaultValueParameterName
        );

        var defaultValue =
            defaultValueIndex >= 0 ? arguments[defaultValueIndex] : ElementDefault(stage, method);

        if (defaultValue is null)
            return arguments;

        var predicate = arguments.Where((_, index) => index != defaultValueIndex).FirstOrDefault();

        return [predicate ?? OmittedArgument(), defaultValue];
    }

    /// <summary>
    /// <c>default(TSource)</c> when it is not <c>null</c>. A struct without a
    /// target-agnostic default keeps the <c>null</c> miss and is reported. The
    /// bare <c>0</c> of an enum that declares no zero member is cast to the
    /// enum, so a target that types its enums can assert it.
    /// </summary>
    private IrExpression? ElementDefault(InvocationExpressionSyntax stage, IMethodSymbol method)
    {
        var elementType = method.TypeArguments[0];
        var elementDefault = DefaultValueLiterals.For(elementType, BuildOrigin);

        if (elementDefault is IrLiteral { Kind: IrLiteralKind.Null })
            return null;

        if (elementDefault is IrLiteral { Kind: IrLiteralKind.Int32 } && IsEnum(elementType))
            return new IrCastExpression(
                elementDefault,
                IrTypeRefMapper.Map(elementType, _originResolver, _target)
            );

        if (elementDefault is not IrLiteral { Kind: IrLiteralKind.Default })
            return elementDefault;

        UnrepresentableMissValue.Report(stage, method, elementType);

        return null;
    }

    private static bool IsEnum(ITypeSymbol type) => type.TypeKind is TypeKind.Enum;
}
