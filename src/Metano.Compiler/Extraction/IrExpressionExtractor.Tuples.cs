using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C# tuple literals — <c>(a, b)</c> and <c>(x: 1, y: 2)</c> — in value
/// position, extracted as <see cref="IrTupleLiteral"/>: the elements in source
/// order plus the tuple type the literal produces. Element names only exist at
/// compile time: reads by name or as <c>ItemN</c> resolve to the element
/// position (<see cref="SymbolHelper.GetPositionalTupleElementIndex"/>).
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
