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
}
