namespace Metano.Tests;

/// <summary>#708: binding a receiver once preserves the enum type of the stored computation.</summary>
public class BoundEnumStoreTests
{
    private const string Source = """
        [Transpile, EmitInFile("numeric-enum-bound-store")]
        public enum I { A, B, C }
        [Transpile, EmitInFile("numeric-enum-bound-store")]
        public enum B : byte { Zero, Max = 255 }
        [Transpile, EmitInFile("numeric-enum-bound-store"), System.Flags]
        public enum F { None, One, Two }
        [Transpile, EmitInFile("numeric-enum-bound-store")]
        public enum L : long { Zero, Max = long.MaxValue }
        [Transpile, EmitInFile("numeric-enum-bound-store")]
        public enum U : uint { Zero, Two = 2, Max = uint.MaxValue }
        [Transpile, EmitInFile("numeric-enum-bound-store")]
        public class Box
        {
            public I Mode { get; set; }
            public B Narrow { get; set; }
            public F Flags { get; set; }
            public L Wide { get; set; }
            public static Box Get(Box value) => value;
            public static int Idx() => 0;
            public static int BoundedLoop()
            {
                var count = 0;
                for (var e = I.A; e < I.C; e++) count++;
                return count;
            }
            public static int CheckedBoundedLoop()
            {
                var count = 0;
                checked { for (var e = I.C; e >= I.A; e--) count = unchecked(count + 1); }
                return count;
            }
            public static void MutatedLoop()
            {
                for (var e = I.A; e < I.C; e++) e += 1;
            }
            public static void UIntLoop()
            {
                for (var e = U.Zero; e <= U.Two; e++) { }
            }
            public static void UnboundedUIntLoop()
            {
                for (var e = U.Max; e <= U.Max; e++) { }
            }
            public static void Compound(Box[] boxes, Box value, I[] ints, B[] bytes, F[] flags, L[] longs)
            {
                boxes[0].Mode += 1; Get(value).Mode -= 2; ints[Idx()] += 1;
                boxes[0].Narrow += 1; Get(value).Narrow -= 2; bytes[Idx()] += 1;
                boxes[0].Flags += 1; Get(value).Flags -= 2; flags[Idx()] += 1;
                boxes[0].Wide += 1; Get(value).Wide -= 2; longs[Idx()] += 1;
            }
            public static void Increment(Box[] boxes, Box value, I[] ints, B[] bytes, F[] flags, L[] longs)
            {
                boxes[0].Mode++; Get(value).Mode++; ints[Idx()]++;
                boxes[0].Narrow++; Get(value).Narrow++; bytes[Idx()]++;
                boxes[0].Flags++; Get(value).Flags++; flags[Idx()]++;
                boxes[0].Wide++; Get(value).Wide++; longs[Idx()]++;
            }
        }
        """;

    [Test]
    public async Task BoundStores_PreserveEnumAssertions()
    {
        var files = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden(
            "numeric-enum-bound-store.ts",
            files["numeric-enum-bound-store.ts"]
        );
    }

    [Test]
    public async Task BoundedEnumSteps_UseTheExistingProofAndRejectBodyWrites()
    {
        var output = TranspileHelper.Transpile(Source)["numeric-enum-bound-store.ts"];

        await Assert.That(output).Contains("for (let e = I.A as I; e < I.C; (e as number)++)");
        await Assert.That(output).Contains("for (let e = I.C as I; e >= I.A; (e as number)--)");

        await Assert
            .That(output)
            .Contains("for (let e = I.A as I; e < I.C; e = ((e + 1) | 0) as I)");

        await Assert
            .That(output)
            .Contains("for (let e = U.Zero as U; e <= U.Two; (e as number)++)");

        await Assert
            .That(output)
            .Contains("for (let e = U.Max as U; e <= U.Max; e = (e + 1) >>> 0 as U)");
    }

    [Test]
    public async Task ProjectCheckedBoundStores_TypeCheckStrictly()
    {
        var (files, _) = TranspileHelper.TranspileWithOverflowChecks(Source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
