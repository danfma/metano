using System.Text;
using Metano.Compiler.Analysis;
using Metano.Compiler.Dart;
using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests.IR;

/// <summary>
/// The 64-bit integer IR nodes of spec 012 (<see cref="IrNumericConversion"/>,
/// <see cref="IrIntegerOverflow"/>) are walked, rewritten and printed by every
/// stage: TypeScript renders the reduction around the operand and Dart
/// renders the operand, keeping the operand's own grouping.
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
        new IrNumericEndpoint(IrNumericKind.Int32),
        new IrNumericEndpoint(IrNumericKind.Int64),
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
    public async Task TypeScript_RendersTheWrappedOperand()
    {
        var mapped = IrToTsExpressionBridge.Map(
            ProductOfWrappedSum,
            IrToTsBridgeContext.For(bclRegistry: null)
        );

        await Assert
            .That(new Metano.Compiler.TypeScript.Printer().PrintExpressionFragment(mapped))
            .IsEqualTo("a * BigInt.asIntN(64, b + c)");
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
    public async Task DartRewriter_KeepsTheWrapperAndRewritesItsOperand()
    {
        var containerlessRead = new IrMemberAccess(
            new IrTypeReference("Totals"),
            "Count",
            new IrMemberOrigin("Totals", "Count", IsStatic: true, IsDeclaringTypeNoContainer: true)
        );

        var rewriter = new DartIrRewriter(DeclarativeMappingRegistry.Empty);

        var rewritten = rewriter.Rewrite(Wrapped(containerlessRead));

        await Assert
            .That(OperandOf(rewritten))
            .IsEqualTo(
                containerlessRead with
                {
                    Origin = containerlessRead.Origin! with { IsDeclaringTypeNoContainer = false },
                }
            );
    }

    [Test]
    public async Task JsxPropRewrite_KeepsTheWrapperAndRewritesItsOperand()
    {
        var referenced = new HashSet<string>();
        var propRead = new IrMemberAccess(new IrThisExpression(), "Count");

        var rewritten = IrToTsJsxComponentBridge.Rewrite(
            Wrapped(propRead),
            propNames: ["Count"],
            referenced
        );

        await Assert.That(OperandOf(rewritten)).IsEqualTo(new IrIdentifier("props$count"));
        await Assert.That(referenced).IsEquivalentTo(["Count"]);
    }

    [Test]
    public async Task PlainObjectThisRewrite_KeepsTheWrapperAndRewritesItsOperand()
    {
        var fieldRead = new IrMemberAccess(new IrThisExpression(), "id");

        var rewritten = IrToTsPlainObjectBridge.RewriteThis(Wrapped(fieldRead), "self");

        await Assert
            .That(OperandOf(rewritten))
            .IsEqualTo(new IrMemberAccess(new IrIdentifier("self"), "id"));
    }

    [Test]
    public async Task OutArgumentBinding_KeepsTheWrapperAndBindsItsOperand()
    {
        var outCall = new IrOutArgumentCall(
            new IrCallExpression(new IrIdentifier("tryRead"), []),
            [
                new IrOutArgument(
                    0,
                    "value",
                    new IrOutLocalDeclaration("value", new IrPrimitiveTypeRef(IrPrimitive.Int32)),
                    new IrLiteral(0, IrLiteralKind.Int32)
                ),
            ],
            new IrPrimitiveTypeRef(IrPrimitive.Int32)
        );

        var lowered = new ExpressionVariableLowering().Lower(
            new IrReturnStatement(Wrapped(outCall))
        );

        var binding = (IrTupleDeconstruction)lowered[0];

        await Assert.That(lowered.Count).IsEqualTo(2);
        await Assert
            .That(string.Join(",", binding.Elements.Select(e => e.Name)))
            .IsEqualTo("$result0,value");
        await Assert
            .That(lowered[1])
            .IsEqualTo(new IrReturnStatement(Wrapped(new IrIdentifier("$result0"))));
    }

    [Test]
    public async Task DartUInt64Literal_PrintsTheSameTwosComplementBits()
    {
        var max = new IrLiteral(ulong.MaxValue, IrLiteralKind.UInt64);
        var small = new IrLiteral(42UL, IrLiteralKind.UInt64);

        await Assert.That(PrintDart(max)).IsEqualTo("-1");
        await Assert.That(PrintDart(small)).IsEqualTo("42");
    }

    [Test]
    public async Task Dart_GroupsANegativeLiteralReceiver()
    {
        var call = new IrCallExpression(
            new IrMemberAccess(new IrLiteral(ulong.MaxValue, IrLiteralKind.UInt64), "toString"),
            []
        );

        await Assert.That(PrintDart(call)).IsEqualTo("(-1).toString()");
    }

    [Test]
    public async Task Dart_GroupsANegativeLiteralOperandOfANegation()
    {
        var negated = new IrUnaryExpression(
            IrUnaryOp.Negate,
            new IrLiteral(-2128831035, IrLiteralKind.Int32)
        );

        await Assert.That(PrintDart(negated)).IsEqualTo("-(-2128831035)");
    }

    [Test]
    public async Task Dart_LeavesAPositiveLiteralReceiverBare()
    {
        var call = new IrCallExpression(
            new IrMemberAccess(new IrLiteral(5UL, IrLiteralKind.UInt64), "toString"),
            []
        );

        await Assert.That(PrintDart(call)).IsEqualTo("5.toString()");
    }

    private static IrExpression Wrapped(IrExpression operand) =>
        new IrIntegerOverflow(
            new IrNumericConversion(
                operand,
                new IrNumericEndpoint(IrNumericKind.Int32),
                new IrNumericEndpoint(IrNumericKind.Int64),
                IsChecked: false
            ),
            IrNumericKind.Int64,
            IrOverflowMode.Wrap
        );

    /// <summary>The operand inside <see cref="Wrapped"/>, asserting both wrappers survived.</summary>
    private static IrExpression OperandOf(IrExpression? expression) =>
        expression is IrIntegerOverflow { Value: IrNumericConversion conversion }
            ? conversion.Operand
            : throw new InvalidOperationException(
                $"Expected the wrappers to survive, got {expression?.GetType().Name ?? "null"}."
            );

    private static string PrintDart(IrExpression expression)
    {
        var sb = new StringBuilder();

        IrBodyPrinter.PrintExpression(sb, expression);

        return sb.ToString();
    }
}
