using System.Collections.Immutable;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The element type C# gives a list or array literal
/// (<see cref="IrArrayLiteral.ElementType"/>), kept only where a target that
/// infers a literal's type from its elements would get a narrower one:
/// <c>List&lt;int?&gt; l = [1]</c>, <c>List&lt;Animal&gt; l = [dog]</c>,
/// <c>List&lt;int&gt; l = []</c>, <c>new int?[] { 1 }</c>. A literal whose
/// elements all have the element type, or whose position already declares its
/// type (an argument, a member's return value, a field or property
/// initializer, the value of an assignment), carries none.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrArrayLiteral ArrayLiteralOf(
        ExpressionSyntax creation,
        InitializerExpressionSyntax initializer
    )
    {
        var elements = initializer.Expressions.Select(Extract).ToList();

        if (_semantic.GetOperation(creation) is not IArrayCreationOperation operation)
            return new IrArrayLiteral(elements);

        var values = operation.Initializer?.ElementValues ?? [];

        return new IrArrayLiteral(elements, DeclaredElementType(creation, operation.Type, values));
    }

    private IrTypeRef? DeclaredElementType(
        ExpressionSyntax literal,
        ITypeSymbol? collectionType,
        ImmutableArray<IOperation> elements
    )
    {
        if (ElementTypeOf(collectionType) is not { } elementType)
            return null;

        var inferredFromElements =
            elements.Length > 0 && elements.All(element => Yields(element, elementType));

        return inferredFromElements || IsTypedByItsPosition(literal)
            ? null
            : IrTypeRefMapper.Map(elementType, _originResolver, _target);
    }

    /// <summary>The element type of a list or one-dimensional array type.</summary>
    private static ITypeSymbol? ElementTypeOf(ITypeSymbol? collectionType) =>
        collectionType is null or IArrayTypeSymbol { Rank: > 1 }
            ? null
            : CollectionElementTypeOf(collectionType);

    /// <summary>
    /// Whether <paramref name="element"/> alone says the element type: its own
    /// type (a spread's element type) is that type, or converts to it
    /// numerically (a spread tuple, element by element) — the conversion is
    /// part of the emitted element. Any other
    /// conversion (a cast included) leaves no trace in the emitted element, so
    /// its operand answers; a <c>default</c> is <c>null</c> unless the element
    /// type is a non-nullable value type.
    /// </summary>
    private bool Yields(IOperation element, ITypeSymbol elementType) =>
        element switch
        {
            ISpreadOperation spread => IsConvertedOnSpread(spread, elementType)
                || IsExactly(
                    spread.ElementType,
                    elementType,
                    spread.ElementType?.NullableAnnotation == NullableAnnotation.Annotated
                ),
            IConversionOperation conversion => IsEmittedNumerically(conversion)
                || Yields(conversion.Operand, elementType),
            IDefaultValueOperation => IsNonNullableValueType(elementType),
            _ => IsExactly(element.Type, elementType, MayBeNull(element)),
        };

    /// <summary>
    /// Whether each spread element is emitted as its conversion to
    /// <paramref name="elementType"/>: a number, or a tuple converted element
    /// by element.
    /// </summary>
    private static bool IsConvertedOnSpread(ISpreadOperation spread, ITypeSymbol elementType)
    {
        var convertedTuples = TupleElementConversions.Implicit.ConvertingTuples(
            spread.ElementType,
            elementType
        );

        return spread.ElementConversion.IsNumeric
            || (elementType.IsTupleType && convertedTuples is not null);
    }

    private static bool IsEmittedNumerically(IConversionOperation conversion)
    {
        var applied = conversion.GetConversion();

        return applied.IsNumeric || applied.IsConstantExpression;
    }

    private bool MayBeNull(IOperation value) =>
        _semantic.GetTypeInfo(value.Syntax).Nullability.FlowState == NullableFlowState.MaybeNull;

    /// <summary>
    /// The same type, nested nullability included
    /// (<c>(int, string)</c> is not <c>(int, string?)</c>), and not a non-null
    /// reference where <paramref name="elementType"/> is its nullable form (a
    /// nullable value type is a type of its own).
    /// </summary>
    private static bool IsExactly(ITypeSymbol? type, ITypeSymbol elementType, bool mayBeNull)
    {
        var sameType = SymbolEqualityComparer.IncludeNullability.Equals(
            type?.WithNullableAnnotation(elementType.NullableAnnotation),
            elementType
        );

        var holdsItsNull =
            mayBeNull
            || elementType.IsValueType
            || elementType.NullableAnnotation != NullableAnnotation.Annotated;

        return sameType && holdsItsNull;
    }

    /// <summary>
    /// Whether the position of <paramref name="literal"/> declares its type,
    /// looking through the expressions that hand their own position to an
    /// operand (parentheses, a conditional's branches, <c>??</c>, a
    /// <c>switch</c> expression's arms, an enclosing literal's elements). A
    /// local's initializer and a lambda's result declare none in a target that
    /// infers them.
    /// </summary>
    private static bool IsTypedByItsPosition(ExpressionSyntax literal)
    {
        var position = PositionOf(literal);

        return position.Parent switch
        {
            ArgumentSyntax or InitializerExpressionSyntax => true,
            AssignmentExpressionSyntax assignment => assignment.Right == position,
            ArrowExpressionClauseSyntax => true,
            ReturnStatementSyntax returned => !ReturnsFromALambda(returned),
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => declarator
                .Parent
                ?.Parent is BaseFieldDeclarationSyntax,
            EqualsValueClauseSyntax => true,
            _ => false,
        };
    }

    private static SyntaxNode PositionOf(SyntaxNode node) =>
        node.Parent is { } parent && HandsItsPositionTo(parent, node) ? PositionOf(parent) : node;

    private static bool HandsItsPositionTo(SyntaxNode parent, SyntaxNode operand) =>
        parent switch
        {
            ParenthesizedExpressionSyntax or SwitchExpressionArmSyntax => true,
            ExpressionElementSyntax or CollectionExpressionSyntax => true,
            InitializerExpressionSyntax initializer => initializer.IsKind(
                SyntaxKind.ArrayInitializerExpression
            ),
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax => operand
                is InitializerExpressionSyntax,
            SwitchExpressionSyntax @switch => @switch.GoverningExpression != operand,
            ConditionalExpressionSyntax conditional => conditional.Condition != operand,
            BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.CoalesceExpression),
            _ => false,
        };

    private static bool ReturnsFromALambda(ReturnStatementSyntax returned) =>
        returned
            .Ancestors()
            .FirstOrDefault(ancestor =>
                ancestor
                    is AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax
                        or MemberDeclarationSyntax
            ) is AnonymousFunctionExpressionSyntax;
}
