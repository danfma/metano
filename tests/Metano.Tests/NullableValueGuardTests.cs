namespace Metano.Tests;

/// <summary>
/// The missing-value throw of a <c>Nullable&lt;T&gt;</c> read or unwrap cast
/// (#629). TypeScript carries it on every <c>x.Value</c> and <c>(T)x</c>,
/// whatever nullable analysis says about <c>x</c>: its flow state is
/// optimistic (after <c>x!</c>, for a local a lambda clears, for a property
/// checked and then cleared by a call, for an awaited value) and absent in
/// nullable-disabled code. Only a value wrapped where it is written
/// (<c>((int?)5).Value</c>, <c>(int)(int?)x</c>) is read bare. Dart keeps
/// deciding from the flow state: its analyzer rejects a guard over a
/// promoted local.
/// </summary>
public class NullableValueGuardTests
{
    private const string Guard =
        " ?? (() => {\n    throw new Error(\"Nullable object must have a value.\");\n  })()";

    private const string Source = """
        #nullable enable
        #pragma warning disable CS8629
        using System;
        using System.Threading.Tasks;
        using Metano.Annotations;

        [Transpile]
        public class Holder
        {
            public int? P { get; set; }

            public void Clear() => P = null;
        }

        [Transpile, NoContainer]
        public static class Unwraps
        {
            public static int CheckedLocal(int? n)
            {
                if (n.HasValue)
                    return n.Value;

                return -1;
            }

            public static int Suppressed(int? n) => n!.Value;

            public static int LambdaCleared(int? n)
            {
                n = 1;
                Action clear = () => n = null;
                clear();

                return n.Value;
            }

            public static int ClearedByACall(Holder h)
            {
                if (h.P.HasValue)
                {
                    h.Clear();

                    return h.P.Value;
                }

                return -1;
            }

            public static async Task<int> Awaited(Task<int?> pending)
            {
                int? n = await pending;

                return n.Value;
            }

            public static double ToDouble(long? nl) => (double)nl;

            public static int SuppressedCast(int? n) => (int)n!;

            public static string Flag(bool? b) => (bool)b ? "t" : "f";

            public static (long, long) Widened((int, int)? t) => ((long, long))t;

            public static int WrappedCast(int x) => (int)(int?)x;

            public static int WrappedConstant() => ((int?)5).Value;

        #nullable disable
            public static int Disabled(int? n) => n.Value;
        #nullable restore
        }
        """;

    private const string UserDefinedConversionSource = """
        #nullable enable
        #pragma warning disable CS8629
        using Metano.Annotations;

        [Transpile]
        public readonly record struct Meters(int Count)
        {
            public static explicit operator int?(Meters meters) =>
                meters.Count > 0 ? meters.Count : null;
        }

        [Transpile, NoContainer]
        public static class Conversions
        {
            public static int Read(Meters m) => ((int?)m).Value;

            public static int Cast(Meters m) => (int)(int?)m;
        }
        """;

    private const string DartSource = """
        #nullable enable
        #pragma warning disable CS8629
        using Metano.Annotations;

        [Transpile]
        public class Unwraps
        {
            public int CheckedLocal(int? n)
            {
                if (n.HasValue)
                    return n.Value;

                return -1;
            }

            public int Unchecked(int? n) => n.Value;

            public int Suppressed(int? n) => n!.Value;

            public int CheckedCast(double? d) => d.HasValue ? (int)d : -1;

            public int UncheckedCast(double? d) => (int)d;

            public int WrappedConstant() => ((int?)5).Value;

        #nullable disable
            public int Disabled(int? n) => n.Value;
        #nullable restore
        }
        """;

    private static readonly string Unwraps = TranspileHelper.Transpile(Source)["unwraps.ts"];

    [Test]
    public async Task CheckedLocal_KeepsTheGuard() =>
        await Assert
            .That(Unwraps)
            .Contains(
                "  if (n != null) {\n    return n ?? (() => {\n      throw new Error(\"Nullable object must have a value.\");\n    })();\n  }"
            );

    [Test]
    public async Task NullForgivingRead_KeepsTheGuard() =>
        await Assert
            .That(BodyOf("suppressed(n: number | null): number"))
            .IsEqualTo($"  return n{Guard};");

    [Test]
    public async Task LambdaClearedLocal_KeepsTheGuard() =>
        await Assert
            .That(BodyOf("lambdaCleared(n: number | null): number"))
            .Contains($"  return n{Guard};");

    [Test]
    public async Task PropertyClearedByACall_KeepsTheGuard() =>
        await Assert.That(Unwraps).Contains("    h.clear();\n\n    return h.p ?? (() => {");

    [Test]
    public async Task NullableDisabledRead_KeepsTheGuard() =>
        await Assert
            .That(BodyOf("disabled(n: number | null): number"))
            .IsEqualTo($"  return n{Guard};");

    [Test]
    public async Task AwaitedValue_KeepsTheGuard() =>
        await Assert
            .That(
                BodyOf("async function awaited(pending: Promise<number | null>): Promise<number>")
            )
            .Contains($"  return n{Guard};");

    [Test]
    public async Task UnwrapCasts_KeepTheGuard()
    {
        await Assert
            .That(BodyOf("toDouble(nl: bigint | null): number"))
            .IsEqualTo($"  return Number(nl{Guard});");

        await Assert
            .That(BodyOf("suppressedCast(n: number | null): number"))
            .IsEqualTo($"  return n{Guard};");

        await Assert
            .That(BodyOf("flag(b: boolean | null): string"))
            .IsEqualTo($"  return b{Guard} ? \"t\" : \"f\";");
    }

    [Test]
    public async Task WholeTupleCast_KeepsTheGuard() =>
        await Assert
            .That(BodyOf("widened(t: [number, number] | null): [bigint, bigint]"))
            .Contains($"(t{Guard})");

    [Test]
    public async Task ValueWrappedWhereItIsWritten_IsReadBare()
    {
        await Assert.That(BodyOf("wrappedCast(x: number): number")).IsEqualTo("  return x;");
        await Assert.That(BodyOf("wrappedConstant(): number")).IsEqualTo("  return 5;");
    }

    /// <summary>
    /// A user-defined conversion operator to <c>T?</c> returns whatever its
    /// body does, so its result is not a value wrapped where it is written.
    /// </summary>
    [Test]
    public async Task UserDefinedConversionToNullable_KeepsTheGuard()
    {
        var conversions = TranspileHelper.Transpile(UserDefinedConversionSource)["conversions.ts"];

        await Assert
            .That(conversions)
            .Contains($"export function read(m: Meters): number {{\n  return m{Guard};\n}}");

        await Assert
            .That(conversions)
            .Contains($"export function cast(m: Meters): number {{\n  return m{Guard};\n}}");
    }

    [Test]
    public async Task EveryUnwrap_MatchesTheGoldenOutput() =>
        await TranspileHelper.AssertGolden("nullable-value-guards.ts", Unwraps);

    /// <summary>
    /// Dart is unchanged by #629: a read nullable analysis does not report
    /// as maybe-null stays bare, and the golden pins the output of the
    /// predicate that was there before.
    /// </summary>
    [Test]
    public async Task Dart_DecidesFromTheFlowState_AsBefore()
    {
        var (files, _) = DartTranspileHelper.Transpile(DartSource);

        await TranspileHelper.AssertGolden("nullable-value-guards.dart", files["unwraps.dart"]);
    }

    /// <summary>The body of the exported function whose signature is <paramref name="signature"/>.</summary>
    private static string BodyOf(string signature)
    {
        var start = Unwraps.IndexOf(signature + " {\n", StringComparison.Ordinal);
        var bodyStart = start + signature.Length + " {\n".Length;
        var end = Unwraps.IndexOf("\n}\n", bodyStart, StringComparison.Ordinal);

        return start < 0 ? "" : Unwraps[bodyStart..end];
    }
}
