namespace Metano.Tests;

/// <summary>
/// A member that redeclares a name an emitted base declares carries the TypeScript
/// <c>override</c> modifier whatever its accessibility, so a package compiled with
/// <c>noImplicitOverride</c> accepts it (TS4114/TS4116, #759).
/// </summary>
public class ImplicitOverrideTests
{
    private const string Source = """
        [assembly: TranspileAssembly]

        namespace App
        {
            public abstract class Shape
            {
                protected abstract double Area();

                protected virtual string Label() => "shape";

                protected abstract int Sides { get; }

                public virtual string Name => "shape";

                public static int Count() => 0;

                public string Describe() => Label() + Area() + Sides + Name;
            }

            public abstract class Polygon : Shape
            {
                protected abstract override double Area();
            }

            public sealed class Square : Polygon
            {
                protected override double Area() => 4;

                protected override string Label() => "square";

                protected override int Sides => 4;

                public override string Name => "square";

                public static new int Count() => 1;
            }

            public closed class Op
            {
                protected virtual int Weight() => 0;

                public int Total() => Weight();
            }

            public sealed class Add : Op
            {
                protected override int Weight() => 1;
            }

            public sealed class Neg : Op
            {
                protected override int Weight() => 2;
            }
        }
        """;

    [Test]
    public async Task ProtectedAndAbstractOverrides_CarryTheOverrideModifier()
    {
        var result = TranspileHelper.Transpile(Source + ClosedFamilyUnionTests.ClosedPolyfill);

        await Assert
            .That(result["app/polygon.ts"])
            .Contains("  protected abstract override area(): number;");

        var square = result["app/square.ts"];

        await Assert.That(square).Contains("  protected override area(): number {");
        await Assert.That(square).Contains("  protected override label(): string {");
        await Assert.That(square).Contains("  override get sides(): number {");
        await Assert.That(square).Contains("  override get name(): string {");
        await Assert.That(square).Contains("  static override count(): number {");
        await Assert.That(result["app/add.ts"]).Contains("  protected override weight(): number {");
    }

    [Test]
    public async Task Output_TypeChecksUnderNoImplicitOverride()
    {
        var result = TranspileHelper.Transpile(Source + ClosedFamilyUnionTests.ClosedPolyfill);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            result,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
