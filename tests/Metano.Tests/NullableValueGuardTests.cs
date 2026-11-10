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

            public static int RewrappedNullable(int? n) => ((int?)n).Value;

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

            public static (long, long) Pair(Meters m) => ((long, long))((int?)m, 1);
        }
        """;

    private const string UnboxingSource = """
        #nullable enable
        #pragma warning disable CS8629
        using Metano.Annotations;

        [Transpile, NoContainer]
        public static class Unboxing
        {
            public static int Read(object o) => ((int?)(object)o).Value;

            public static int Cast(object o) => (int)(int?)(object)o;
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

    private static readonly Lazy<string> Unwraps = new(() =>
        TranspileHelper.Transpile(Source)["unwraps.ts"]
    );

    [Test]
    public async Task CheckedLocal_KeepsTheGuard() =>
        await Assert
            .That(Unwraps.Value)
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
        await Assert.That(Unwraps.Value).Contains("    h.clear();\n\n    return h.p ?? (() => {");

    [Test]
    public async Task NullableDisabledRead_KeepsTheGuard() =>
        await Assert
            .That(BodyOf("disabled(n: number | null): number"))
            .IsEqualTo($"  return n{Guard};");

    [Test]
    public async Task AwaitedValue_KeepsTheGuard() =>
        await Assert
            .That(BodyOf("awaited(pending: Promise<number | null>): Promise<number>"))
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
    /// A cast to <c>T?</c> wraps a present value only over a non-nullable
    /// value type: over a nullable operand it is an identity conversion, and
    /// over an <c>object</c> it unboxes a reference that may be <c>null</c>.
    /// </summary>
    [Test]
    public async Task CastToNullableOfAValueThatMayBeMissing_KeepsTheGuard()
    {
        var unboxing = TranspileHelper.Transpile(UnboxingSource)["unboxing.ts"];

        await Assert
            .That(BodyOf("rewrappedNullable(n: number | null): number"))
            .IsEqualTo($"  return n{Guard};");

        await Assert
            .That(unboxing)
            .Contains($"export function read(o: Object): number {{\n  return o{Guard};\n}}");

        await Assert
            .That(unboxing)
            .Contains($"export function cast(o: Object): number {{\n  return o{Guard};\n}}");
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

    /// <summary>
    /// The cast tuple literal shares the predicate: an element converted by
    /// a user-defined operator to <c>T?</c> unwraps with the throw.
    /// </summary>
    [Test]
    public async Task UserDefinedConversionInACastTupleLiteral_KeepsTheGuard()
    {
        var conversions = TranspileHelper.Transpile(UserDefinedConversionSource)["conversions.ts"];

        await Assert.That(conversions).Contains($"  return [BigInt(m{Guard}), ");
    }

    [Test]
    public async Task EveryUnwrap_MatchesTheGoldenOutput() =>
        await TranspileHelper.AssertGolden("nullable-value-guards.ts", Unwraps.Value);

    /// <summary>
    /// Dart decides from the nullable flow state: a read that analysis does
    /// not report as maybe-null (checked, suppressed, nullable-disabled) is
    /// bare, and one it does report carries the throw.
    /// </summary>
    [Test]
    public async Task Dart_OmitsTheGuardWhereFlowStateIsNotMaybeNull()
    {
        var (files, _) = DartTranspileHelper.Transpile(DartSource);

        await TranspileHelper.AssertGolden("nullable-value-guards.dart", files["unwraps.dart"]);
    }

    /// <summary>
    /// The body of the exported function whose signature, from its name to
    /// its return type, is <paramref name="signature"/>; fails when the
    /// output declares no such function.
    /// </summary>
    private static string BodyOf(string signature)
    {
        var output = Unwraps.Value;
        var opening = signature + " {\n";
        var start = output.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
            throw new InvalidOperationException($"No function '{signature}' in:\n{output}");

        var bodyStart = start + opening.Length;
        var end = output.IndexOf("\n}\n", bodyStart, StringComparison.Ordinal);

        return output[bodyStart..end];
    }
}
