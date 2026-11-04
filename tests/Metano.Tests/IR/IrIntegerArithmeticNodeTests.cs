using System.Text;
using Metano.Compiler.Analysis;
using Metano.Compiler.Dart;
using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests.IR;

/// <summary>
/// The 64-bit integer IR nodes of spec 012 (<see cref="IrNumericConversion"/>,
/// <see cref="IrIntegerOverflow"/>) are walked, rewritten and printed by every
/// stage before any extractor produces them (slice 012a): the TypeScript
/// <see cref="Int64Representation.Number"/> mode and Dart render them as their
/// operand, keeping the operand's own grouping.
/// </summary>
public class IrIntegerArithmeticNodeTests
{
    private static readonly IrExpression Sum = new IrBinaryExpression(
        new IrIdentifier("b"),
        IrBinaryOp.Add,
        new IrIdentifier("c")
    );

    private static readonly IrExpression WrappedSum = new IrIntegerOverflow(
        Sum,
        IrNumericKind.Int64,
        IrOverflowMode.Wrap
    );

    private static readonly IrExpression ConvertedSum = new IrNumericConversion(
        Sum,
        IrNumericKind.Int32,
        IrNumericKind.Int64,
        IsChecked: false
    );

    private static readonly IrExpression ProductOfWrappedSum = new IrBinaryExpression(
        new IrIdentifier("a"),
        IrBinaryOp.Multiply,
        WrappedSum
    );

    [Test]
    public async Task Children_AreTheOperand()
    {
        await Assert.That(IrNodeChildren.ExpressionsOf(WrappedSum)).IsEquivalentTo([Sum]);
        await Assert.That(IrNodeChildren.ExpressionsOf(ConvertedSum)).IsEquivalentTo([Sum]);
    }

    [Test]
    public async Task SideEffectFreedom_IsTheOperands()
    {
        var assignment = new IrBinaryExpression(
            new IrIdentifier("x"),
            IrBinaryOp.Assign,
            new IrLiteral(1, IrLiteralKind.Int32)
        );

        var wrappedAssignment = new IrIntegerOverflow(
            assignment,
            IrNumericKind.Int64,
            IrOverflowMode.Check
        );

        await Assert.That(IrEvaluationTraits.IsSideEffectFree(ConvertedSum)).IsTrue();
        await Assert.That(IrEvaluationTraits.IsSideEffectFree(wrappedAssignment)).IsFalse();
    }

    [Test]
    public async Task TypeScriptNumberMode_RendersTheOperandWithItsGrouping()
    {
        var mapped = IrToTsExpressionBridge.Map(
            ProductOfWrappedSum,
            IrToTsBridgeContext.For(bclRegistry: null)
        );

        await Assert
            .That(new Metano.Compiler.TypeScript.Printer().PrintExpressionFragment(mapped))
            .IsEqualTo("a * (b + c)");
    }

    [Test]
    public async Task TypeScriptCoverage_IsTheOperands()
    {
        IReadOnlyList<IrStatement> body = [new IrReturnStatement(ConvertedSum)];

        await Assert.That(IrBodyCoverageProbe.IsFullyCovered(body)).IsTrue();
    }

    [Test]
    public async Task Dart_PrintsTheOperandWithItsGrouping()
    {
        await Assert.That(PrintDart(ProductOfWrappedSum)).IsEqualTo("a * (b + c)");
        await Assert.That(PrintDart(ConvertedSum)).IsEqualTo("b + c");
    }

    [Test]
    public async Task Dart_GroupsAWrappedOperandOfAUnaryOperator()
    {
        var negated = new IrUnaryExpression(IrUnaryOp.Negate, ConvertedSum);

        await Assert.That(PrintDart(negated)).IsEqualTo("-(b + c)");
    }

    [Test]
    public async Task DartRewriter_DescendsIntoTheOperand()
    {
        var rewriter = new DartIrRewriter(DeclarativeMappingRegistry.Empty);

        var rewritten = rewriter.Rewrite(ProductOfWrappedSum);

        await Assert.That(rewritten).IsEqualTo(ProductOfWrappedSum);
    }

    [Test]
    public async Task DartUInt64Literal_PrintsTheSameTwosComplementBits()
    {
        var max = new IrLiteral(ulong.MaxValue, IrLiteralKind.UInt64);
        var small = new IrLiteral(42UL, IrLiteralKind.UInt64);

        await Assert.That(PrintDart(max)).IsEqualTo("-1");
        await Assert.That(PrintDart(small)).IsEqualTo("42");
    }

    private static string PrintDart(IrExpression expression)
    {
        var sb = new StringBuilder();

        IrBodyPrinter.PrintExpression(sb, expression);

        return sb.ToString();
    }
}
