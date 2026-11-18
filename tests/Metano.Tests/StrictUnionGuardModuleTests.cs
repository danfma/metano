namespace Metano.Tests;

/// <summary>
/// Spec 011 FR-029 (stage 011b B2): a top-level <c>[StrictUnionGuard]</c>
/// base's guard lives in its own module, <c>shape.guard.ts</c>, which imports
/// the variants' guards and holds the <c>variants</c> thunk table; the base
/// module never imports a variant (an ESM cycle: a variant extends the base at
/// load) and nothing registers at module load (the registry is retired).
/// </summary>
public class StrictUnionGuardModuleTests
{
    internal const string Source = """
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;

        [assembly: TranspileAssembly]

        namespace Shapes;

        // Co-located with the base: the golden bundle check splits a golden at
        // each module header, and an import-less enum module would merge into
        // the barrel before it.
        [StringEnum, EmitInFile("shape")]
        public enum ShapeKind
        {
            Circle,
            Square,
        }

        [GenerateGuard, StrictUnionGuard]
        [Discriminator("Kind")]
        public abstract record Shape(ShapeKind Kind);

        [GenerateGuard]
        public sealed record Circle(ShapeKind Kind, double Radius) : Shape(Kind);

        [GenerateGuard]
        public sealed record Square(ShapeKind Kind, double Side) : Shape(Kind);
        """;

    [Test]
    public async Task StrictUnion_ReportsNoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task StrictUnion_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden(
            "strict-union-guard-module.ts",
            RuntimeTypeDescriptorTests.TranspileAll(Source)
        );

    [Test]
    public async Task StrictUnion_TheGuardModuleDispatchesThroughTheVariantTable()
    {
        var guard = TranspileHelper.Transpile(Source)["shapes/shape.guard.ts"];

        await Assert.That(guard).Contains("import { isCircle, type Circle } from \"./circle\";");
        await Assert.That(guard).Contains("import { isSquare, type Square } from \"./square\";");

        await Assert
            .That(guard)
            .Contains("const variants = [\n  () => isCircle,\n  () => isSquare,\n];");

        await Assert.That(guard).Contains("export function isShape(value: unknown)");
        await Assert.That(guard).Contains("export function assertShape(");

        await Assert
            .That(guard)
            .Contains("const variant = variants[[\"Circle\", \"Square\"].indexOf(v.kind)];");

        await Assert.That(guard).Contains("return variant !== undefined && variant()(value);");
    }

    [Test]
    public async Task StrictUnion_TheBaseModuleImportsNoVariantAndDeclaresNoGuard()
    {
        var shape = TranspileHelper.Transpile(Source)["shapes/shape.ts"];

        await Assert.That(shape).DoesNotContain("./circle");
        await Assert.That(shape).DoesNotContain("./square");
        await Assert.That(shape).DoesNotContain("isShape");
    }

    [Test]
    public async Task StrictUnion_NothingRegistersAtModuleLoad()
    {
        var files = TranspileHelper.Transpile(Source);

        foreach (var content in files.Values)
        {
            await Assert.That(content).DoesNotContain("registerUnionGuard");
            await Assert.That(content).DoesNotContain("getUnionGuard");
        }
    }

    [Test]
    public async Task StrictUnion_TheBarrelReExportsTheGuardModule()
    {
        var index = TranspileHelper.Transpile(Source)["shapes/index.ts"];

        await Assert
            .That(index)
            .Contains("export { assertShape, isShape } from \"./shape.guard\";");
    }

    /// <summary>
    /// A base whose variants are nested in it declares them in its own file
    /// (the companion namespace), so its strict guard stays there, beside the
    /// <c>variants</c> table, and no guard module is emitted.
    /// </summary>
    [Test]
    public async Task StrictUnion_NestedVariantsKeepTheGuardInTheBaseFile()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]

            [StringEnum]
            public enum ResultKind { Empty, Loaded }

            [GenerateGuard, StrictUnionGuard]
            [Discriminator("Kind")]
            public abstract record Result(ResultKind Kind)
            {
                [GenerateGuard]
                public sealed record Empty(ResultKind Kind) : Result(Kind);

                [GenerateGuard]
                public sealed record Loaded(ResultKind Kind, int Count) : Result(Kind);
            }
            """
        );

        var result = files["result.ts"];

        await Assert.That(files.ContainsKey("result.guard.ts")).IsFalse();
        await Assert.That(result).Contains("export function isResult(value: unknown)");
        await Assert.That(result).Contains("() => Result.isEmpty");
        await Assert.That(result).Contains("() => Result.isLoaded");
    }
}
