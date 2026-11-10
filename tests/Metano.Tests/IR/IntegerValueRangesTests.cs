using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// <see cref="IntegerValueRanges"/> over compiled Roslyn operations: the
/// reading that decides which conversions and reductions the small-integer
/// lowering omits (spec 012 FR-074–FR-077). Each row is the returned
/// expression of one method of <see cref="Source"/>.
/// </summary>
public class IntegerValueRangesTests
{
    private const string Source = """
        public static class Rows
        {
            private const int Mask = 0x7F;
            private static readonly int Runtime = 0x7F;

            public static int Constant() => 300;
            public static int Variable(int x) => x;
            public static int Masked(int x) => x & 0xFF;
            public static int MaskedByConstant(int x) => x & Mask;
            public static int MaskedByField(int x) => x & Runtime;
            public static int BothNegative(int x, short s) => x & s;
            public static int ByteSum(byte a, byte b) => a + b;
            public static int ByteDifference(byte a, byte b) => a - b;
            public static int UShortProduct(ushort a, ushort b) => a * b;
            public static int IntSum(int a, int b) => a + b;
            public static int ByteUnion(byte a, ushort b) => a | b;
            public static int Shifted(byte a) => a << 8;
            public static int ShiftedByVariable(byte a, int n) => a << n;
            public static int ShiftedPastTheMask(byte a) => a << 40;
            public static int Widened(short s) => s;
            public static int FromLong(long l) => (int)l;
            public static int FromDouble(double d) => (int)d;
            public static int FromNullable(byte? b) => (int)b!;
            public static ulong ULongProduct(ulong a, ulong b) => a * b;
            public static int NarrowedULongProduct(ulong a, ulong b) => (int)(a * b);
            public static double Floating(double d) => d;
        }
        """;

    private static readonly Lazy<(SemanticModel Model, SyntaxNode Root)> Compiled = new(Compile);

    [Test]
    [Arguments("Constant", 300L, 300L)]
    [Arguments("Variable", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("Masked", 0L, 255L)]
    [Arguments("MaskedByConstant", 0L, 127L)]
    [Arguments("MaskedByField", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("BothNegative", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("ByteSum", 0L, 510L)]
    [Arguments("ByteDifference", -255L, 255L)]
    [Arguments("UShortProduct", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("IntSum", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("ByteUnion", 0L, 65535L)]
    [Arguments("Shifted", 0L, 65280L)]
    [Arguments("ShiftedByVariable", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("ShiftedPastTheMask", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("Widened", -32768L, 32767L)]
    [Arguments("FromLong", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("FromDouble", (long)int.MinValue, (long)int.MaxValue)]
    [Arguments("FromNullable", 0L, 255L)]
    [Arguments("NarrowedULongProduct", (long)int.MinValue, (long)int.MaxValue)]
    public async Task Of_ReadsTheValuesAnOperationCanProduce(string method, long min, long max)
    {
        var range = IntegerValueRanges.Of(ReturnedOperationOf(method));

        await Assert.That(range?.Min).IsEqualTo((Int128)min);
        await Assert.That(range?.Max).IsEqualTo((Int128)max);
    }

    /// <summary>
    /// A product whose bound does not fit says nothing beyond its type, the
    /// whole <c>ulong</c> range.
    /// </summary>
    [Test]
    public async Task Of_FallsBackToTheTypeWhenAProductIsUnknown()
    {
        var range = IntegerValueRanges.Of(ReturnedOperationOf("ULongProduct"));

        await Assert.That(range).IsEqualTo(IntegerRange.Of(IrNumericKind.UInt64));
    }

    [Test]
    public async Task Of_IsNullForAnOperationThatIsNotAnInteger()
    {
        await Assert.That(IntegerValueRanges.Of(ReturnedOperationOf("Floating"))).IsNull();
        await Assert.That(IntegerValueRanges.Of(null)).IsNull();
    }

    private static IOperation? ReturnedOperationOf(string method)
    {
        var (model, root) = Compiled.Value;

        var declaration = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.Text == method);

        return model.GetOperation(declaration.ExpressionBody!.Expression);
    }

    private static (SemanticModel Model, SyntaxNode Root) Compile()
    {
        var tree = CSharpSyntaxTree.ParseText(
            Source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );

        var compilation = CSharpCompilation.Create(
            "IntegerValueRangesRows",
            [tree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return (compilation.GetSemanticModel(tree), tree.GetRoot());
    }
}
