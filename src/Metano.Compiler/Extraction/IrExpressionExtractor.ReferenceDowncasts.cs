using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The facts of an explicit reference conversion (<c>(Derived)value</c>, #669)
/// a target that erases static types needs to type, and check, the result
/// itself: whether a type test already narrows the operand, whether the target
/// type derives from the enclosing one, whether the cast belongs to an
/// <c>[Inline]</c> expansion, and whether the target names a type parameter.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The facts of the cast when it is an explicit reference conversion;
    /// <c>null</c> for any other conversion.
    /// </summary>
    private IrReferenceConversion? ReferenceConversionOf(
        CastExpressionSyntax cast,
        ITypeSymbol targetType
    )
    {
        var conversion = ConversionOfCast(cast, targetType);

        if (conversion is not { IsReference: true, IsExplicit: true })
            return null;

        return new IrReferenceConversion(
            IsNarrowedByTypeTest: IsNarrowedByTypeTest(cast, targetType),
            TargetDerivesFromEnclosingType: DerivesFromEnclosingType(targetType, cast),
            InInlineExpansion: _inlineExpanding.Count > 0,
            TargetMentionsTypeParameter: MentionsTypeParameter(targetType)
        );
    }

    /// <summary>
    /// The conversion the cast performs; a cast of a <c>dynamic</c> value is
    /// classified as the same cast of an <c>object</c> one (spec 011 FR-030),
    /// since the runtime binder converts from whatever the value holds.
    /// </summary>
    private Conversion ConversionOfCast(CastExpressionSyntax cast, ITypeSymbol targetType)
    {
        if (
            _context.KeepsDynamicSourceForms
            || _semantic.GetTypeInfo(cast.Expression).Type is not IDynamicTypeSymbol
        )
            return _semantic.ClassifyConversion(cast.Expression, targetType);

        var compilation = (CSharpCompilation)_semantic.Compilation;
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);

        return compilation.ClassifyConversion(objectType, targetType);
    }

    private static bool MentionsTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => MentionsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(MentionsTypeParameter),
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="targetType"/> extends a type that encloses
    /// <paramref name="at"/> (a method of <c>Shape</c> casting to its subclass
    /// <c>Circle</c>).
    /// </summary>
    private bool DerivesFromEnclosingType(ITypeSymbol targetType, SyntaxNode at)
    {
        var enclosing = _semantic.GetEnclosingSymbol(at.SpanStart)?.ContainingType;
        var enclosingTypes = new List<INamedTypeSymbol>();

        for (var type = enclosing; type is not null; type = type.ContainingType)
            enclosingTypes.Add(type.OriginalDefinition);

        for (var baseType = targetType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (
                enclosingTypes.Contains(baseType.OriginalDefinition, SymbolEqualityComparer.Default)
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a type test of the cast's operand for the cast's own type
    /// guards it: the cast sits in the right operand of an <c>&amp;&amp;</c>,
    /// the true branch of a <c>?:</c> or the body of an <c>if</c> whose condition
    /// is, or conjoins, <c>operand is Type</c> (also with a designation or a
    /// property pattern).
    /// </summary>
    private bool IsNarrowedByTypeTest(CastExpressionSyntax cast, ITypeSymbol targetType)
    {
        var child = (SyntaxNode)cast;

        foreach (var ancestor in cast.Ancestors())
        {
            if (
                GuardOf(ancestor, child) is { } guard
                && ConjunctsOf(guard).Any(test => TestsOperandAs(test, cast.Expression, targetType))
            )
                return true;

            child = ancestor;
        }

        return false;
    }

    /// <summary>
    /// The condition <paramref name="ancestor"/> holds over <paramref name="child"/>,
    /// when <paramref name="child"/> runs only where it is true.
    /// </summary>
    private static ExpressionSyntax? GuardOf(SyntaxNode ancestor, SyntaxNode child) =>
        ancestor switch
        {
            BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.LogicalAndExpression) && binary.Right == child =>
                binary.Left,
            ConditionalExpressionSyntax conditional when conditional.WhenTrue == child =>
                conditional.Condition,
            IfStatementSyntax ifStatement when ifStatement.Statement == child =>
                ifStatement.Condition,
            _ => null,
        };

    private static IEnumerable<ExpressionSyntax> ConjunctsOf(ExpressionSyntax condition)
    {
        var unwrapped = condition;

        while (unwrapped is ParenthesizedExpressionSyntax parenthesized)
            unwrapped = parenthesized.Expression;

        if (
            unwrapped is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.LogicalAndExpression)
        )
            return [.. ConjunctsOf(binary.Left), .. ConjunctsOf(binary.Right)];

        return [unwrapped];
    }

    /// <summary>
    /// Whether <paramref name="test"/> is <c>operand is T</c> (<c>is T t</c>,
    /// <c>is T { … }</c>) for the same operand and type.
    /// </summary>
    private bool TestsOperandAs(
        ExpressionSyntax test,
        ExpressionSyntax operand,
        ITypeSymbol targetType
    ) =>
        test switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression) =>
                binary.Left.IsEquivalentTo(operand, topLevel: false)
                    && IsType(binary.Right, targetType),
            IsPatternExpressionSyntax isPattern => isPattern.Expression.IsEquivalentTo(
                operand,
                topLevel: false
            )
                && PatternType(isPattern.Pattern) is { } patternType
                && IsType(patternType, targetType),
            _ => false,
        };

    private static TypeSyntax? PatternType(PatternSyntax pattern) =>
        pattern switch
        {
            DeclarationPatternSyntax declaration => declaration.Type,
            RecursivePatternSyntax recursive => recursive.Type,
            TypePatternSyntax typePattern => typePattern.Type,
            _ => null,
        };

    private bool IsType(ExpressionSyntax syntax, ITypeSymbol targetType) =>
        SymbolEqualityComparer.Default.Equals(_semantic.GetTypeInfo(syntax).Type, targetType);
}
