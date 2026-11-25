using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The <c>[GenerateGuard]</c> guards of a closed family lowered to a union (spec 010
/// FR-C5, decision W9-1, #757): every case reads its <c>kind</c> from its class's
/// prototype, so a guard admits instances only, with no structural fallback that would
/// let a plain object of the same shape through. A family that keeps the class shape
/// (<c>MS0073</c>) keeps the structural guards.
/// </summary>
public class ClosedFamilyGuardTests
{
    private const string Family = """
        [assembly: TranspileAssembly]

        [GenerateGuard]
        public closed record Animal(string Name);

        [GenerateGuard]
        public sealed record Dog(string Name, int Age) : Animal(Name);

        [GenerateGuard]
        public closed record Bird(string Name) : Animal(Name);

        [GenerateGuard]
        public sealed record Owl(string Name) : Bird(Name);

        public sealed record Duck(string Name) : Bird(Name);
        """;

    [Test]
    public async Task UnionFamilyGuards_TestInstancesOnly()
    {
        var files = TranspileHelper.Transpile(Family + ClosedFamilyUnionTests.ClosedPolyfill);

        await Assert
            .That(files["animal.ts"])
            .Contains(
                "export function isAnimal(value: unknown): value is Animal {\n"
                    + "  return value instanceof Animal$Base;\n}"
            );

        await Assert
            .That(files["bird.ts"])
            .Contains(
                "export function isBird(value: unknown): value is Bird {\n"
                    + "  return value instanceof Bird$Base;\n}"
            );

        await Assert
            .That(files["dog.ts"])
            .Contains(
                "export function isDog(value: unknown): value is Dog {\n"
                    + "  return value instanceof Dog;\n}"
            );

        await Assert
            .That(files["owl.ts"])
            .Contains(
                "export function isOwl(value: unknown): value is Owl {\n  return value instanceof Owl;\n}"
            );

        foreach (var name in new[] { "animal.ts", "bird.ts", "dog.ts", "owl.ts" })
            await Assert.That(files[name]).DoesNotContain("typeof value !== \"object\"");

        await Assert.That(files["animal.ts"]).Contains("asserts value is Animal");
    }

    [Test]
    public async Task UnionFamilyGuards_TypeCheckStrictly()
    {
        var files = TranspileHelper.Transpile(Family + ClosedFamilyUnionTests.ClosedPolyfill);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ClassShapeFamilyGuards_KeepTheStructuralFallback()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [GenerateGuard]
            public closed record Shape(string Name)
            {
                public string Kind => "shape";
            }

            [GenerateGuard]
            public sealed record Dot(string Name) : Shape(Name);
            """ + ClosedFamilyUnionTests.ClosedPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.ClosedFamilyTagSuppressed);

        await Assert.That(files["shape.ts"]).Contains("if (value instanceof Shape) {");
        await Assert.That(files["shape.ts"]).Contains("typeof value !== \"object\"");
        await Assert.That(files["dot.ts"]).Contains("typeof value !== \"object\"");
    }
}
