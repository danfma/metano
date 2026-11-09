namespace Metano.Tests;

/// <summary>
/// A floating-point value cast to an <c>int</c>, <c>uint</c>, a narrower
/// integer or <c>char</c> truncates and saturates as .NET 9+ does (spec 012
/// FR-070), in both 64-bit modes, where the cast used to be erased.
/// </summary>
public class FloatingPointTruncationTests
{
    /// <summary>
    /// FR-070: a floating-point cast to a narrow integer truncates, and a
    /// constant one folds; a cast to <c>long</c> truncates into a
    /// <c>bigint</c> (FR-015).
    /// </summary>
    [Test]
    public async Task FloatingPointToNarrowInteger_Truncates()
    {
        const string source = """
            [Transpile]
            public class Casts
            {
                public int ToInt(double d) => (int)d;
                public uint ToUInt(float f) => (uint)f;
                public byte ToByte(double d) => (byte)d;
                public short ToShortChecked(double d) => checked((short)d);
                public int Constant() => (int)3.7;
                public long ToLong(double d) => (long)d;
                public int Narrow(long l) => (int)l;
            }
            """;

        var (files, _) = TranspileHelper.TranspileWithDiagnostics(source);

        var output = files["casts.ts"];

        await Assert.That(output).Contains("return int32FromDouble(d);");
        await Assert.That(output).Contains("return uint32FromDouble(f);");
        await Assert.That(output).Contains("return int32FromDouble(d) & 0xFF;");

        await Assert
            .That(output)
            .Contains("return Number(checkedNarrow(BigInt(int32FromDoubleChecked(d)), 16, true));");

        await Assert.That(output).Contains("return 3;");

        await Assert
            .That(output)
            .Contains("toLong(d: number): bigint {\n    return int64FromDouble(d);");

        await Assert
            .That(output)
            .Contains("narrow(l: bigint): number {\n    return Number(BigInt.asIntN(32, l));");

        await Assert
            .That(output)
            .Contains(
                "import { checkedNarrow, int32FromDouble, int32FromDoubleChecked, "
                    + "int64FromDouble, uint32FromDouble } from \"metano-runtime\";"
            );
    }

    /// <summary>
    /// Dart keeps the explicit cast where it does not convert: a nullable
    /// value cast to its underlying type reads through that type first.
    /// </summary>
    [Test]
    public async Task Dart_NullableUnwrap_ReadsThroughTheUnderlyingType()
    {
        const string source = """
            [Transpile]
            public class Unwrap
            {
                public int ToInt(double? d) => (int)d;
            }
            """;

        var (files, _) = DartTranspileHelper.Transpile(source);

        await Assert.That(files["unwrap.dart"]).Contains("(d as double).toInt().toSigned(32)");
    }

    private const string ForEachSource = """
        [Transpile]
        public class Loops
        {
            public int Last(double[] doubles)
            {
                var last = 0;
                foreach (int x in doubles)
                    last = x;
                return last;
            }

            public int LastChecked(double[] doubles)
            {
                var last = 0;
                checked
                {
                    foreach (int x in doubles)
                        last = x;
                }
                return last;
            }
        }
        """;

    /// <summary>
    /// A <c>foreach</c> variable converted from a floating-point element
    /// truncates too (FR-070's "every source"), and checks
    /// inside a <c>checked</c> block.
    /// </summary>
    [Test]
    public async Task ForEach_FloatingPointElement_Truncates()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(ForEachSource);

        var output = files["loops.ts"];

        await Assert.That(output).Contains("const x = int32FromDouble(x$element);");
        await Assert.That(output).Contains("const x = int32FromDoubleChecked(x$element);");
    }
}
