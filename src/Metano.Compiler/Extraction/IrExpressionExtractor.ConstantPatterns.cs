using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The value facts of a constant pattern or <c>case</c> label: the boxing facts
/// of spec 011 FR-050 and whether the constant's value is a floating-point NaN,
/// read from the semantic model so every spelling of the constant agrees.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrConstantPattern WithConstantFacts(
        IrConstantPattern pattern,
        ExpressionSyntax constant,
        ITypeSymbol? inputType
    ) => WithBoxedConstantFacts(pattern with { IsNaN = HoldsNaN(constant) }, constant, inputType);

    private IrConstantCaseLabel WithConstantFacts(CaseSwitchLabelSyntax label) =>
        WithBoxedConstantFacts(label) with
        {
            IsNaN = HoldsNaN(label.Value),
        };

    private bool HoldsNaN(ExpressionSyntax constant) =>
        _semantic.GetConstantValue(constant) is { HasValue: true, Value: var value }
        && value switch
        {
            double number => double.IsNaN(number),
            float number => float.IsNaN(number),
            _ => false,
        };
}
