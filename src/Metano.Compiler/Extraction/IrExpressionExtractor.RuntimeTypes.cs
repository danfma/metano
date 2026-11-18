using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>x.GetType()</c> and <c>typeof(T)</c> (spec 011 FR-018, FR-025): the
/// neutral nodes a target renders as a runtime type, with the constant fold the
/// receiver's static type decides. The reflection the targets cannot answer
/// (<c>typeof(void)</c>, an open generic definition, a pointer) is reported as
/// <c>MS0049</c> by <see cref="Frontend.Roslyn.RuntimeQuestionScanner"/>, so
/// extraction only leaves the placeholder.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>receiver.GetType()</c> as an <see cref="IrGetTypeExpression"/>;
    /// <c>null</c> for any other call.
    /// </summary>
    private IrGetTypeExpression? TryExtractGetType(
        InvocationExpressionSyntax inv,
        IMethodSymbol? method
    )
    {
        if (_context.KeepsReflectionSourceForms || !IsObjectGetType(method))
            return null;

        var receiverSyntax = (inv.Expression as MemberAccessExpressionSyntax)?.Expression;
        var receiver = receiverSyntax is null ? new IrThisExpression() : Extract(receiverSyntax);

        var receiverType = receiverSyntax is null
            ? null
            : _semantic.GetTypeInfo(receiverSyntax).Type;

        return new IrGetTypeExpression(receiver, FoldedRuntimeTypeOf(receiverType, receiver))
        {
            ReceiverType = receiverType is null
                ? null
                : IrTypeRefMapper.Map(receiverType, _originResolver, _target),
        };
    }

    /// <summary>
    /// Whether <paramref name="method"/> is <c>object.GetType()</c>, or the
    /// <c>Exception.GetType()</c> that hides it (it returns the same type).
    /// </summary>
    internal static bool IsObjectGetType(IMethodSymbol? method) =>
        method is { Name: nameof(GetType), Parameters.Length: 0, IsStatic: false }
        && (
            method.ContainingType.SpecialType == SpecialType.System_Object
            || method.ContainingType.ToDisplayString() == "System.Exception"
        );

    /// <summary>
    /// The type a <c>GetType()</c> receiver of <paramref name="receiverType"/>
    /// always has: a non-nullable primitive, enum or struct whose read has no
    /// side effect. A sealed class is not folded (its value may be <c>null</c>
    /// and throws), nor is a generic struct or a branded wrapper.
    /// </summary>
    /// <summary>
    /// Whether <paramref name="type"/> mentions a type parameter, whose runtime
    /// type only the value knows.
    /// </summary>
    private static bool IsOpen(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => IsOpen(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(IsOpen),
            _ => false,
        };

    private IrTypeRef? FoldedRuntimeTypeOf(ITypeSymbol? receiverType, IrExpression receiver)
    {
        if (
            receiverType is not { IsValueType: true }
            || receiverType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            || IsOpen(receiverType)
            || !IrEvaluationTraits.IsSideEffectFree(receiver)
        )
            return null;

        return IrTypeRefMapper.Map(receiverType, _originResolver, _target) switch
        {
            IrPrimitiveTypeRef
            {
                Primitive: not (IrPrimitive.Object or IrPrimitive.Void)
            } primitive => primitive,
            // A closed generic struct folds to its specialization (spec 011 FR-023).
            IrNamedTypeRef
            {
                Semantics.Kind: IrNamedTypeKind.NumericEnum
                    or IrNamedTypeKind.StringEnum
                    or IrNamedTypeKind.Struct
                    or IrNamedTypeKind.Record,
            } named => named,
            _ => null,
        };
    }

    /// <summary>
    /// <c>typeof(T)</c> for a type a target can describe; the type parameters
    /// and the 2.x collection types (whose descriptors arrive with stage 011c)
    /// report <c>MS0001</c>, and the reflection no target answers is left as
    /// a placeholder for the scanner's <c>MS0049</c>.
    /// </summary>
    private IrExpression ExtractTypeOf(TypeOfExpressionSyntax typeOf)
    {
        var type = _semantic.GetTypeInfo(typeOf.Type).Type;

        if (_context.KeepsReflectionSourceForms)
            return ReportUnsupported(typeOf, typeOf.Kind().ToString());

        if (type is null || RuntimeQuestionFacts.IsUnsupportedTypeOf(type))
            return new IrUnsupportedExpression(typeOf.Kind().ToString());

        if (RuntimeQuestionFacts.MentionsTypeParameter(type) || IsCollectionType(type))
            return ReportUnsupported(typeOf, typeOf.Kind().ToString());

        return new IrTypeOfExpression(IrTypeRefMapper.Map(type, _originResolver, _target));
    }

    private static bool IsCollectionType(ITypeSymbol type) =>
        type is INamedTypeSymbol { ContainingNamespace: { } containing }
        && containing.ToDisplayString().StartsWith("System.Collections", StringComparison.Ordinal);

    /// <summary>
    /// The static type of a tested expression, so the type test is classified
    /// with it (<see cref="IrRuntimeTypeTest.Of"/>); <c>null</c> when unknown.
    /// </summary>
    private IrTypeRef? StaticTypeOf(ExpressionSyntax expression) =>
        _semantic.GetTypeInfo(expression).Type is { } type
            ? IrTypeRefMapper.Map(type, _originResolver, _target)
            : null;

    /// <summary>The static type of the value <paramref name="pattern"/> tests.</summary>
    private IrTypeRef? PatternInputType(SyntaxNode pattern) =>
        _semantic.GetOperation(pattern) is IPatternOperation { InputType: { } inputType }
            ? IrTypeRefMapper.Map(inputType, _originResolver, _target)
            : null;
}
