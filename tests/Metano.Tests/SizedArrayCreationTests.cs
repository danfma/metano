using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>new T[n]</c> fills every slot with the element type's default, so the
/// TypeScript array is typed as its element and filled with a value of that
/// type; an element whose default no shared literal spells stays unsupported.
/// </summary>
public class SizedArrayCreationTests
{
    private const string Source = """
        [Transpile]
        public enum Rank : long { First = 1 }

        [Transpile]
        public enum Level { Low, High }

        [Transpile]
        public class Arrays
        {
            public decimal[] Decimals(int n) => new decimal[n];
            public Rank[] Ranks() => new Rank[2];
            public Level[] Levels() => new Level[2];
            public T[] References<T>(int n) where T : class => new T[n];
        }
        """;

    [Test]
    public async Task DecimalElements_AreTypedAsDecimal()
    {
        var arrays = TranspileHelper.Transpile(Source)["arrays.ts"];

        await Assert
            .That(arrays)
            .Contains("return new Array<Decimal>(n).fill(new Decimal(\"0\"));");
    }

    [Test]
    public async Task EnumWithoutAZeroMember_FillsATypedZero()
    {
        var arrays = TranspileHelper.Transpile(Source)["arrays.ts"];

        await Assert.That(arrays).Contains("return new Array<Rank>(2).fill(0 as Rank);");
        await Assert.That(arrays).Contains("return new Array<Level>(2).fill(Level.Low);");
    }

    [Test]
    public async Task ReferenceTypeParameterElements_FillNull()
    {
        var arrays = TranspileHelper.Transpile(Source)["arrays.ts"];

        await Assert.That(arrays).Contains("return new Array(n).fill(null);");
    }

    /// <summary>
    /// <c>default(T)</c> is <c>0</c> for <c>T = int</c> and <c>null</c> for a
    /// class, so an unconstrained (or <c>struct</c>) type-parameter element
    /// has no slot value to share and stays <c>MS0001</c>.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("where T : struct")]
    public async Task ValueOrUnconstrainedTypeParameterElements_StayUnsupported(string constraint)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public class Arrays
            {
                public T[] Slots<T>(int n) {{constraint}} => new T[n];
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert.That(unsupported[0]).Contains("new T[n]");
    }
}
