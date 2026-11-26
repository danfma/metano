namespace Metano.Tests;

/// <summary>
/// <c>==</c> / <c>!=</c> over a nullable tuple compares as C# does (#415):
/// a missing tuple equals only another missing one, a present one compares
/// element by element, and both operands are evaluated before the null test.
/// It reported MS0001 before.
/// </summary>
public class LiftedTupleEqualityTests
{
    private const string Source = """
        using System;

        [Transpile]
        public static class Lifted
        {
            public static bool OneSide((int, int)? t, int a) => t == (a, 2);

            public static bool OneSideNegated((int, int)? t, int a) => t != (a, 2);

            public static bool BothSides((int, int)? t, (int, int)? u) => t == u;

            public static bool Evaluated(Func<(int, int)?> next, Func<int> side) => next() == (1, side());
        }
        """;

    [Test]
    public async Task OneNullableSide_TestsItBeforeTheElements()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);
        var output = files["lifted.ts"];

        await Assert.That(output).Contains("return !(t == null) && t[0] === a && t[1] === 2;");
        await Assert.That(output).Contains("return t == null || t[0] !== a || t[1] !== 2;");

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task TwoNullableSides_AreEqualWhenBothAreMissing()
    {
        var output = TranspileHelper.Transpile(Source)["lifted.ts"];

        await Assert
            .That(output)
            .Contains(
                "return t == null || u == null ? t == null && u == null : t[0] === u[0] && t[1] === u[1];"
            );
    }

    [Test]
    public async Task OperandsWithSideEffects_AreEvaluatedFirst()
    {
        var output = TranspileHelper.Transpile(Source)["lifted.ts"];

        await Assert
            .That(output)
            .Contains("const tuple$temp$0 = next();\n    const tuple$temp$1 = side();");
    }
}
