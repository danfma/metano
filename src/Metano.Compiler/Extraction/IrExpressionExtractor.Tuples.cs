using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C# tuple literals — <c>(a, b)</c> and <c>(x: 1, y: 2)</c> — in value
/// position, extracted as <see cref="IrTupleLiteral"/>: the elements in source
/// order plus the tuple type the literal produces. Element names only exist at
/// compile time: reads by name or as <c>ItemN</c> resolve to the element
/// position (<see cref="SymbolHelper.GetPositionalTupleElement"/>).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrExpression ExtractTupleLiteral(TupleExpressionSyntax tuple)
    {
        // `(a, b) = (b, a)`, `(var x, var y) = t`: the tuple is an assignment
        // target, not a value. Deconstruction into existing variables is not
        // modelled (spec 003, research D7).
        if (IsDeconstructionTarget(tuple))
            return ReportUnsupported(tuple, "tuple deconstruction");

        if (tuple.Arguments.Count > SymbolHelper.MaxFlatTupleArity)
        {
            return ReportUnsupported(
                tuple,
                $"tuple literal with more than {SymbolHelper.MaxFlatTupleArity} elements"
            );
        }

        var elements = tuple.Arguments.Select(argument => Extract(argument.Expression)).ToList();

        return new IrTupleLiteral(elements, TupleLiteralType(tuple));
    }

    /// <summary>
    /// Reports MS0001 for a write to a C# tuple element (<c>t.Item1 = 5</c>,
    /// <c>t.x += 1</c>, <c>t.x++</c>) and returns the placeholder; <c>null</c>
    /// when <paramref name="target"/> is not a tuple element.
    /// <para>
    /// A C# tuple is a value type: assigning, passing or returning it copies
    /// it, so a write never shows through another variable. The TypeScript
    /// array is shared by reference, so the write would leak into every alias,
    /// and a Dart record is immutable, so the write does not compile. Until
    /// copy semantics exist, the write is rejected on every target.
    /// </para>
    /// </summary>
    private IrExpression? TryReportTupleElementWrite(SyntaxNode write, ExpressionSyntax target)
    {
        if (
            _semantic.GetSymbolInfo(target).Symbol
            is not IFieldSymbol { ContainingType.IsTupleType: true }
        )
            return null;

        return ReportUnsupported(
            write,
            "tuple element assignment",
            $"Expression '{write}' writes a tuple element; tuples lower to shared, "
                + "immutable values, so element writes are not supported by the transpiler."
        );
    }

    /// <summary>The operand of a <c>++</c> / <c>--</c>, prefix or postfix; <c>null</c> otherwise.</summary>
    private static ExpressionSyntax? IncrementedOperand(ExpressionSyntax node) =>
        node switch
        {
            PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression)
                    || postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
            _ => null,
        };

    /// <summary>
    /// Whether <paramref name="tuple"/> (or the outermost tuple nesting it)
    /// is the left side of an assignment or the variable of a
    /// <c>foreach</c>.
    /// </summary>
    private static bool IsDeconstructionTarget(TupleExpressionSyntax tuple)
    {
        SyntaxNode outermost = tuple;

        while (outermost.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax enclosing })
            outermost = enclosing;

        return outermost.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == outermost,
            ForEachVariableStatementSyntax loop => loop.Variable == outermost,
            _ => false,
        };
    }

    /// <summary>
    /// The tuple type the literal produces: the one it converts to when that
    /// is a tuple (<c>(string?, int) t = (null, 1)</c>, whose natural type
    /// has no type for <c>null</c>), else its natural type; <c>null</c> when
    /// neither maps to a tuple type.
    /// </summary>
    private IrTupleTypeRef? TupleLiteralType(TupleExpressionSyntax tuple)
    {
        var typeInfo = _semantic.GetTypeInfo(tuple);

        var type = typeInfo.ConvertedType is { IsTupleType: true } converted
            ? converted
            : typeInfo.Type;

        return type is null
            ? null
            : IrTypeRefMapper.Map(type, _originResolver, _target) as IrTupleTypeRef;
    }
}
