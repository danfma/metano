namespace Metano.Tests;

/// <summary>
/// A type with a static constructor (spec 001 FR-066, #681) that declares
/// user-defined operators and conversions (baseline FR-078, #599, #600):
/// every static helper runs the lazy initialization guard first, as any
/// static member does, a derived class's redeclared conversion is both
/// <c>override</c> and guarded, and a static the constructor assigns is read
/// through its accessor inside a conversion body.
/// </summary>
public class StaticConstructorOperatorTests
{
    private const string Source = """
        [Transpile]
        public class Meter
        {
            public static readonly int Scale;

            static Meter()
            {
                Scale = 10;
            }

            public int V;

            public Meter(int v)
            {
                V = v;
            }

            public static Meter operator +(Meter a, Meter b) => new(a.V + b.V);

            public static Meter operator +(Meter a, int k) => new(a.V + k);

            public static Meter operator -(Meter a) => new(-a.V);

            public static explicit operator int(Meter m) => m.V * Scale;

            public static implicit operator Meter(int v) => new(v);
        }

        [Transpile]
        public class SubMeter : Meter
        {
            public static readonly int Offset;

            static SubMeter()
            {
                Offset = 1;
            }

            public SubMeter(int v)
                : base(v) { }

            public static explicit operator int(SubMeter m) => m.V + Offset;
        }

        [Transpile]
        public struct Pt
        {
            public static readonly int Origin;

            static Pt()
            {
                Origin = 1;
            }

            public int X;

            public Pt(int x)
            {
                X = x;
            }

            public static Pt operator -(Pt a, Pt b) => new(a.X - b.X);
        }
        """;

    [Test]
    public async Task OperatorsAndConversions_RunTheLazyGuardFirst()
    {
        var result = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("static-constructor-operators.ts", result["meter.ts"]);

        await TranspileHelper.AssertGolden(
            "static-constructor-operators-derived.ts",
            result["sub-meter.ts"]
        );

        await TranspileHelper.AssertGolden(
            "static-constructor-operators-struct.ts",
            result["pt.ts"]
        );
    }

    [Test]
    [Arguments("static __addMeterMeter(a: Meter, b: Meter): Meter {")]
    [Arguments("static __addMeterInt32(a: Meter, k: number): Meter {")]
    [Arguments("static __add(...args: unknown[]): Meter {")]
    [Arguments("static __negate(a: Meter): Meter {")]
    [Arguments("static __toInt32(m: Meter): number {")]
    [Arguments("static __implicitFromInt32(v: number): Meter {")]
    public async Task EachStaticHelper_StartsWithTheGuard(string header)
    {
        var meter = TranspileHelper.Transpile(Source)["meter.ts"];

        await Assert.That(meter).Contains($"{header}\n    Meter.#$init();\n");
    }

    [Test]
    public async Task TheDerivedConversion_IsAGuardedOverride()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["sub-meter.ts"])
            .Contains("static override __toInt32(m: SubMeter): number {\n    SubMeter.#$init();\n");
    }

    [Test]
    public async Task ClassInstanceForms_AreNotGuarded_StructInstanceForms_Are()
    {
        var meter = TranspileHelper.Transpile(Source)["meter.ts"];
        var pt = TranspileHelper.Transpile(Source)["pt.ts"];

        await Assert.That(meter).Contains("$negate(): Meter {\n    return Meter");
        await Assert.That(meter).Contains("$add(...args: unknown[]): Meter {\n    if (");
        await Assert.That(pt).Contains("$subtract(b: Pt): Pt {\n    Pt.#$init();\n");
    }

    [Test]
    public async Task AConversionBody_ReadsAConstructorAssignedStaticThroughItsAccessor()
    {
        var meter = TranspileHelper.Transpile(Source)["meter.ts"];

        await Assert.That(meter).Contains("return Math.imul(m.v, Meter.scale);");
    }
}
