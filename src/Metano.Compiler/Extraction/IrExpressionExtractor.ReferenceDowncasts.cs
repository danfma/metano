using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Which explicit reference conversions (<c>(Derived)value</c>) a target
/// that erases static types has to type, and check, itself (#669). A cast the
/// source has already guarded with a type test keeps the bare operand, which the
/// target's own flow analysis narrows.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Whether the cast is an explicit reference conversion whose result the
    /// target types: not one an earlier type test of the same operand already
    /// narrows (<c>o is Money &amp;&amp; ((Money)o).Cents == 0</c>), and not one
    /// to a type that mentions a type parameter inside an <c>[Inline]</c>
    /// expansion, which lands in the caller, where the callee's type parameter
    /// is not in scope.
    /// </summary>
    private bool IsTypedReferenceDowncast(CastExpressionSyntax cast, ITypeSymbol targetType) =>
        _semantic.ClassifyConversion(cast.Expression, targetType)
            is { IsReference: true, IsExplicit: true }
        && !(_inlineExpanding.Count > 0 && MentionsTypeParameter(targetType))
        && !IsNarrowedByTypeTest(cast, targetType);

    /// <summary>
    /// Whether the typed downcast may reference the target's runtime class to
    /// check the cast. Not inside an <c>[Inline]</c> expansion, whose caller's
    /// enclosing type is out of sight, and not in a type the target derives
    /// from (<see cref="DerivesFromEnclosingType"/>).
    /// </summary>
    private bool ChecksReferenceDowncast(CastExpressionSyntax cast, ITypeSymbol targetType) =>
        _inlineExpanding.Count == 0 && !DerivesFromEnclosingType(targetType, cast);

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
    /// <c>Circle</c>): the target's module imports the enclosing one to extend
    /// it, so the enclosing module cannot import the target's class back to
    /// check the cast without a load-time import cycle.
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
