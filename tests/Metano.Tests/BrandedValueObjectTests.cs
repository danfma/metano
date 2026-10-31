namespace Metano.Tests;

/// <summary>
/// Value-object patterns on <c>[Branded]</c> structs (issue #245, ADR-0005):
/// reading the wrapped value collapses to the receiver, identity unwrap
/// conversions are not emitted, and static overload groups go through the
/// overload dispatcher instead of silently dropping siblings.
/// </summary>
public class BrandedValueObjectTests
{
    private const string EpisodeSlugSource = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        [Branded]
        public readonly record struct EpisodeSlug
        {
            private EpisodeSlug(string value) => Value = value;

            public string Value { get; }

            public static EpisodeSlug From(string value)
            {
                if (value.Trim().Length == 0)
                    throw new System.ArgumentException("empty");
                return new EpisodeSlug(value.Trim());
            }

            public static EpisodeSlug From(int number) => new EpisodeSlug("ep-" + number);

            public static implicit operator string(EpisodeSlug self) => self.Value;

            public static explicit operator EpisodeSlug(string raw) => From(raw);
        }
        """;

    [Test]
    public async Task Companion_DispatchesStaticOverloadsAndSkipsUnwrapConversion()
    {
        var result = TranspileHelper.Transpile(EpisodeSlugSource);

        var output = result["episode-slug.ts"];
        var expected = TranspileHelper.ReadExpected("branded-value-object.ts");
        await Assert.That(output).IsEqualTo(expected);
    }

    [Test]
    public async Task ValueRead_CollapsesToReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct EpisodeSlug
            {
                private EpisodeSlug(string value) => Value = value;

                public string Value { get; }
            }

            [Branded]
            public readonly record struct ShotNumber(int Value);

            [Branded]
            public readonly record struct Seconds
            {
                private Seconds(double value) => Value = value;

                public double Value { get; }
            }

            public static class Uses
            {
                public static string Raw(EpisodeSlug slug) => slug.Value;

                public static int Length(EpisodeSlug slug) => slug.Value.Length;

                public static bool Same(ShotNumber a, ShotNumber b) => a.Value == b.Value;

                public static double Total(Seconds a, Seconds b) => a.Value + b.Value;
            }
            """
        );

        var output = result["uses.ts"];
        await Assert.That(output).Contains("return slug;");
        await Assert.That(output).Contains("return slug.length;");
        await Assert.That(output).Contains("return a === b;");
        await Assert.That(output).Contains("return a + b;");
        await Assert.That(output).DoesNotContain(".value");
    }

    [Test]
    public async Task ValueRead_ThroughNullConditional_CollapsesToReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct EpisodeSlug
            {
                private EpisodeSlug(string value) => Value = value;

                public string Value { get; }
            }

            public class Episode
            {
                public EpisodeSlug Slug { get; init; }
            }

            public static class Uses
            {
                public static string? Raw(EpisodeSlug? slug) => slug?.Value;

                public static int? Length(EpisodeSlug? slug) => slug?.Value.Length;

                public static int? SlugLength(Episode? episode) => episode?.Slug.Value.Length;
            }
            """
        );

        var output = result["uses.ts"];

        await Assert.That(output).Contains("return slug;");
        await Assert.That(output).Contains("return slug?.length ?? null;");
        await Assert.That(output).Contains("return episode?.slug.length ?? null;");
        await Assert.That(output).DoesNotContain(".value");
    }

    [Test]
    public async Task ValueRead_OnCrossPackageBrandedType_CollapsesToReceiver()
    {
        // The library wrapper reaches the consumer as a metadata symbol (no
        // syntax), so the value-member rule must be symbol-sourced.
        var library = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            [Branded]
            public readonly record struct ShotNumber(int Value);
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public static class Shots
            {
                public static bool Same(Lib.ShotNumber a, Lib.ShotNumber b) => a.Value == b.Value;
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);

        var output = result["app/shots.ts"];
        await Assert.That(output).Contains("return a === b;");
    }

    [Test]
    public async Task ValueRead_OnNonWrapperShapes_IsKept()
    {
        // Two public value members disqualify the wrapper (it falls back to a
        // regular record), and a plain class `Value` is an ordinary property:
        // neither read may collapse.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Money(long Cents, string Currency);

            public class Box
            {
                public string Value { get; set; } = "";
            }

            public static class Uses
            {
                public static long Cents(Money money) => money.Cents;

                public static string Read(Box box) => box.Value;
            }
            """
        );

        var output = result["uses.ts"];
        await Assert.That(output).Contains("return money.cents;");
        await Assert.That(output).Contains("return box.value;");
    }

    [Test]
    public async Task Companion_KeepsWrapConversion_WhenGroupedWithUnwrap()
    {
        // Both conversions are `op_Implicit`, so the extractor folds them into
        // one overload group; dropping the unwrap leaves a single wrap
        // function, not a dispatcher.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct ScriptWordId(string Value)
            {
                public static implicit operator string(ScriptWordId self) => self.Value;

                public static implicit operator ScriptWordId(string raw) => new(raw);
            }
            """
        );

        var output = result["script-word-id.ts"];
        await Assert
            .That(output)
            .Contains("export function op_Implicit(raw: string): ScriptWordId");
        await Assert.That(output).DoesNotContain("self: ScriptWordId");
        await Assert.That(output).DoesNotContain("...args");
    }

    [Test]
    public async Task Companion_StaticOverloads_UseBrandedParameterTypeCheck()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Seconds
            {
                private Seconds(double value) => Value = value;

                public double Value { get; }

                public static Seconds Combine(double value) => new(value);

                public static Seconds Combine(Seconds a, Seconds b) => new(a.Value + b.Value);
            }
            """
        );

        var output = result["seconds.ts"];
        await Assert.That(output).Contains("export function combine(value: number): Seconds;");
        await Assert
            .That(output)
            .Contains("export function combine(a: Seconds, b: Seconds): Seconds;");
        await Assert.That(output).Contains("return Seconds.create(a + b);");
        await Assert.That(output).Contains("return combineValue(args[0] as number);");
    }

    [Test]
    public async Task ValueRead_OnClassFallbackWrapper_IsKept()
    {
        // A wrapper over a non-primitive can't lower to a branded primitive:
        // it falls back to a class, so `.value` is a real property there and
        // the read must not collapse — neither at a consumer nor inside the
        // wrapper's own members.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public record Inner(int X);

            [Branded]
            public readonly record struct Boxed(Inner Value)
            {
                public int Get() => Value.X;
            }

            public static class Uses
            {
                public static int Read(Boxed b) => b.Value.X;
            }
            """
        );

        await Assert.That(result["uses.ts"]).Contains("return b.value.x;");
        await Assert.That(result["boxed.ts"]).Contains("return this.value.x;");
    }

    [Test]
    public async Task Companion_SkipsNullableAnnotatedUnwrapConversion()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Slug(string Value)
            {
                public static implicit operator string?(Slug s) => s.Value;
            }
            """
        );

        var output = result["slug.ts"];
        await Assert.That(output).DoesNotContain("op_Implicit");
    }

    [Test]
    public async Task Companion_FastPathNames_AvoidUserStaticNames()
    {
        // `From(string value)` would name its fast path `fromValue`, which the
        // user static `FromValue(int)` already claims in the same namespace.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Slug(string Value)
            {
                public static Slug From(string value) => new(value);

                public static Slug From(int number) => new("n" + number);

                public static Slug FromValue(int x) => new("v" + x);
            }
            """
        );

        var output = result["slug.ts"];
        await Assert.That(output).Contains("function fromValue$(value: string): Slug");
        await Assert.That(output).Contains("return fromValue$(args[0] as string);");
        await Assert.That(output).Contains("export function fromValue(x: number): Slug");
    }

    [Test]
    public async Task Companion_GenericStaticOverloads_DeclareTypeParametersPerSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Slug(string Value)
            {
                public static T Pick<T>(T a) => a;

                public static T Pick<T>(T a, T b) => b;
            }
            """
        );

        var output = result["slug.ts"];
        await Assert.That(output).Contains("export function pick<T>(a: T): T;");
        await Assert.That(output).Contains("export function pick<T>(a: T, b: T): T;");
        await Assert.That(output).Contains("export function pick(...args: unknown[]): any");
        await Assert.That(output).Contains("return pickAB(args[0] as any, args[1] as any);");
    }

    [Test]
    public async Task Companion_OverloadsIndistinguishableAfterBrandErasure_DispatchToFirstMatch()
    {
        // Known limitation: `Slug` erases to `string` at runtime, so the
        // dispatcher can't tell `From(string)` from `From(Slug)` and the
        // first sibling in declaration order wins. This pins the behavior.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Slug(string Value)
            {
                public static Slug From(string value) => new(value);

                public static Slug From(Slug other) => new(other.Value + "!");
            }
            """
        );

        var output = result["slug.ts"];
        var firstBranch = output.IndexOf("return fromValue(", StringComparison.Ordinal);
        var secondBranch = output.IndexOf("return fromOther(", StringComparison.Ordinal);
        await Assert.That(firstBranch).IsGreaterThan(-1);
        await Assert.That(secondBranch).IsGreaterThan(firstBranch);
    }
}
