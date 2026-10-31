using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C#'s <c>/</c> over integral operands truncates toward zero and throws on a
/// zero divisor; JS's <c>/</c> is always floating-point. Integer division
/// lowers to the runtime <c>intDiv</c> helper (#307, legacy FR-014).
/// </summary>
public class IntegerDivisionTranspileTests
{
    [Test]
    public async Task IntegralDivision_LowersToTheRuntimeHelper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Quotients
            {
                public static int Average(int total, int count) => total / count;

                public static long Halve(long value) => value / 2;

                public static uint Split(uint value, uint parts) => value / parts;

                public static int Bytes(byte a, short b) => a / b;

                public static int? Lifted(int? a, int b) => a / b;

                public static int Mixed(int a, int b) => a / b * 2 + (a + 1) / b;

                public static int Shrink(int value)
                {
                    value /= 3;
                    return value;
                }

                public static double Ratio(int a, double b) => a / b;

                public static double Real(double a, double b) => a / b;
            }
            """
        );

        await Assert
            .That(result["quotients.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("integer-division.ts"));
    }

    [Test]
    public async Task IntegerDivision_OnlyInsideLoopsAndTry_IsImported()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Halver
            {
                public static int Steps(int n)
                {
                    var steps = 0;
                    for (var i = n; i > 0; i /= 2)
                        steps++;
                    while (n > 0)
                        n -= n / 3 + 1;
                    foreach (var x in new[] { 9, 18 })
                        steps += x / 4;
                    do
                    {
                        steps += steps / 2;
                    } while (steps < 0);
                    try
                    {
                        steps += steps / 5;
                    }
                    finally
                    {
                        n /= 2;
                    }
                    return steps + n;
                }
            }
            """
        );

        await Assert
            .That(result["halver.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("integer-division-in-loops.ts"));
    }

    [Test]
    [Arguments("for (var i = n; i > 0; i /= 2) { }")]
    [Arguments("for (var i = 0; i < n / 2; i++) { }")]
    [Arguments("while (n > 1) n = n / 2;")]
    [Arguments("do { n = n / 2; } while (n > 1);")]
    [Arguments("foreach (var x in new[] { n }) n = x / 2;")]
    [Arguments("try { n = n / 2; } finally { }")]
    [Arguments("try { } catch (System.Exception) { n = n / 2; }")]
    [Arguments("try { } finally { n = n / 2; }")]
    public async Task IntegerDivision_OnlyInsideOneStatement_IsImported(string statement)
    {
        var output = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public static class Halver
            {
                public static int Run(int n)
                {
                    {{statement}}
                    return n;
                }
            }
            """
        )["halver.ts"];

        await Assert.That(output).StartsWith("import { intDiv } from \"metano-runtime\";");
    }

    [Test]
    public async Task CompoundDivision_OnADictionaryEntry_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Tally
            {
                public static void Halve(Dictionary<string, int> counts) => counts["a"] /= 2;
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsTrue();
    }

    [Test]
    public async Task DecimalDivision_KeepsTheDecimalMethod()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Money
            {
                public static decimal Share(decimal total, decimal parts) => total / parts;
            }
            """
        )["money.ts"];

        await Assert.That(output).Contains("return total.div(parts);");
        await Assert.That(output).DoesNotContain("intDiv");
    }

    [Test]
    public async Task BigIntegerDivision_KeepsTheNativeOperator()
    {
        var output = TranspileHelper.Transpile(
            """
            using System.Numerics;

            [Transpile]
            public static class Big
            {
                public static BigInteger Halve(BigInteger value, BigInteger two) => value / two;
            }
            """
        )["big.ts"];

        await Assert.That(output).Contains("return value / two;");
        await Assert.That(output).DoesNotContain("intDiv");
    }

    [Test]
    public async Task UserDefinedDivisionOperator_IsNotIntegerDivision()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Meters(int Value)
            {
                public static Meters operator /(Meters a, int b) => new(a.Value / b);

                public static Meters Third(Meters m) => m / 3;
            }
            """
        )["meters.ts"];

        await Assert.That(output).Contains("new Meters(intDiv(a.value, b))");
        await Assert.That(output).Contains("return m / 3;");
    }

    [Test]
    public async Task CompoundDivision_OnAFieldOfAnImpureReceiver_EvaluatesTheReceiverOnce()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public int Value;

                public static Counter Pick(Counter c) => c;

                public static void Halve(Counter c) => Pick(c).Value /= 2;
            }
            """
        )["counter.ts"];

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) => receiver$temp$0.value = intDiv(receiver$temp$0.value, 2))(Counter.pick(c));"
            );
    }

    [Test]
    public async Task CompoundDivision_OnAnElement_EvaluatesAnImpureIndexOnce()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Cells
            {
                public static int Halve(int[] cells, int index)
                {
                    cells[index++] /= 2;
                    cells[index] /= 2;
                    return index;
                }
            }
            """
        )["cells.ts"];

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) => cells[receiver$temp$0] = intDiv(cells[receiver$temp$0], 2))(index++);"
            );
        await Assert.That(output).Contains("cells[index] = intDiv(cells[index], 2);");
    }

    [Test]
    public async Task CompoundDivision_OnAnExtensionProperty_UsesIntegerDivision()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int Raw;
            }

            [Transpile]
            public static class BoxExtensions
            {
                extension(Box box)
                {
                    public int Size
                    {
                        get => box.Raw;
                        set => box.Raw = value;
                    }
                }

                public static void Halve(Box box) => box.Size /= 2;
            }
            """
        )["box-extensions.ts"];

        await Assert.That(output).Contains("size$set(box, intDiv(size$get(box), 2));");
    }

    [Test]
    public async Task NullConditionalCompoundDivision_UsesIntegerDivision()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public int Value;

                public static void Halve(Counter? c) => c?.Value /= 2;
            }
            """
        )["counter.ts"];

        await Assert.That(output).Contains("c.value = intDiv(c.value, 2)");
    }
}
