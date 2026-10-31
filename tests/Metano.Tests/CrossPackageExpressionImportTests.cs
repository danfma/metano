using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Issue #286 (legacy FR-027–FR-031): a consumer that references a producer
/// <c>[EmitPackage]</c> type only inside an expression body — never in a signature —
/// still imports it, with the value-or-type-only qualifier the position needs, merged
/// into the package's single import line, honoring the producer's
/// <c>[EmitPackage(SubpathRoot = …)]</c> (#267).
/// </summary>
public class CrossPackageExpressionImportTests
{
    private const string FullSpecifier = "@acme/domain/acme/domain/episodes";

    private const string StrippedSpecifier = "@acme/domain/episodes";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ObjectCreation_ImportsConstructedTypeAsValue(bool withSubpathRoot)
    {
        var output = TranspileUsage(
            "public static object Make() => new EntityId(\"x\");",
            withSubpathRoot
        );

        await Assert
            .That(output)
            .Contains($"import {{ EntityId }} from \"{Specifier(withSubpathRoot)}\"");

        await Assert.That(output).Contains("return new EntityId(\"x\");");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EnumMemberAccess_ImportsEnumAsValue(bool withSubpathRoot)
    {
        var output = TranspileUsage(
            "public static object Kind() => EpisodeKind.Feature;",
            withSubpathRoot
        );

        await Assert
            .That(output)
            .Contains($"import {{ EpisodeKind }} from \"{Specifier(withSubpathRoot)}\"");

        await Assert.That(output).Contains("return EpisodeKind.Feature;");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StaticMemberAccess_ImportsDeclaringTypeAsValue(bool withSubpathRoot)
    {
        var output = TranspileUsage(
            "public static int Twice() => Calc.Twice(2) + Calc.Zero;",
            withSubpathRoot
        );

        await Assert
            .That(output)
            .Contains($"import {{ Calc }} from \"{Specifier(withSubpathRoot)}\"");

        await Assert.That(output).Contains("return Calc.twice(2) + Calc.zero;");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExportedAsModuleCall_LowersToBareImportedFunction(bool withSubpathRoot)
    {
        var output = TranspileUsage(
            "public static int Count() => EpisodeModule.Count(3);",
            withSubpathRoot
        );

        await Assert
            .That(output)
            .Contains($"import {{ count }} from \"{Specifier(withSubpathRoot)}\"");

        await Assert.That(output).Contains("return count(3);");
        await Assert.That(output).DoesNotContain("EpisodeModule");
    }

    [Test]
    public async Task TypePattern_ImportsTestedTypeAsValue()
    {
        var output = TranspileUsage("public static bool IsCircle(object o) => o is Circle;");

        await Assert.That(output).Contains($"import {{ Circle }} from \"{FullSpecifier}\"");
        await Assert.That(output).Contains("return o instanceof Circle;");
    }

    [Test]
    public async Task NamespaceQualifiedEnumAccess_DropsNamespaceAndImportsEnum()
    {
        var output = TranspileUsage(
            "public static object Kind() => Acme.Domain.Episodes.EpisodeKind.Short;"
        );

        await Assert.That(output).Contains($"import {{ EpisodeKind }} from \"{FullSpecifier}\"");
        await Assert.That(output).Contains("return EpisodeKind.Short;");
    }

    [Test]
    public async Task BrandedConstruction_ImportsCompanionAsValue()
    {
        var output = TranspileUsage("public static object Id() => new UserId(\"u\");");

        await Assert.That(output).Contains($"import {{ UserId }} from \"{FullSpecifier}\"");
        await Assert.That(output).Contains("UserId.create(\"u\")");
    }

    [Test]
    public async Task NestedTypeAccess_ImportsTopLevelContainer()
    {
        var output = TranspileUsage("public static object Mode() => Season.Mode.Live;");

        await Assert.That(output).Contains($"import {{ Season }} from \"{FullSpecifier}\"");
        await Assert.That(output).Contains("return Season.Mode.Live;");
    }

    [Test]
    public async Task ExpressionAndSignatureReferences_MergeIntoOneLineWithPerNameQualifier()
    {
        var output = TranspileUsage(
            """
            public static Circle? Keep(Circle? c) => c;
            public static object Make() => new EntityId("x");
            public static object Kind() => EpisodeKind.Feature;
            """
        );

        await Assert
            .That(output)
            .Contains($"import {{ EntityId, EpisodeKind, type Circle }} from \"{FullSpecifier}\"");

        await Assert.That(OccurrencesOf(output, FullSpecifier)).IsEqualTo(1);
    }

    [Test]
    public async Task SameTypeInSignatureAndExpression_ImportsAsValue()
    {
        var output = TranspileUsage("public static EpisodeKind Kind() => EpisodeKind.Feature;");

        await Assert.That(output).Contains($"import {{ EpisodeKind }} from \"{FullSpecifier}\"");
        await Assert.That(output).DoesNotContain("type EpisodeKind");
    }

    [Test]
    public async Task NestedTypeInSignature_ImportsTopLevelContainerAsType()
    {
        var output = TranspileUsage("public static Season.Mode? Keep(Season.Mode? m) => m;");

        await Assert.That(output).Contains($"import type {{ Season }} from \"{FullSpecifier}\"");
    }

    [Test]
    public async Task ExpressionOnlyReference_AddsPackageDependency()
    {
        var libCompilation = TranspileHelper.CompileLibrary(Library(withSubpathRoot: false));

        var consumerCompilation = TranspileHelper.CompileConsumer(
            Consumer("public static object Kind() => EpisodeKind.Feature;"),
            libCompilation
        );

        var transformer = TranspileHelper.NewTransformer(consumerCompilation);

        transformer.TransformAll();

        await Assert.That(transformer.CrossPackageDependencies["@acme/domain"]).IsEqualTo("^1.4.0");
    }

    [Test]
    public async Task ExpressionOnlyReference_ToLibraryWithoutEmitPackage_ReportsMs0007()
    {
        var library = """
            [assembly: TranspileAssembly]

            namespace Lib;

            public enum Shade { Light, Dark }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public static class Palette
            {
                public static object Pick() => Lib.Shade.Dark;
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            library,
            consumer
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.CrossPackageResolution
                    && d.Message.Contains("Lib.Shade")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task IntraProject_NamespaceQualifiedAccessAndModuleCall_UseLocalBindings()
    {
        var output = TranspileHelper.Transpile(
            """
            namespace App.Mods
            {
                [Transpile, ExportedAsModule]
                public static class Counter
                {
                    public static int Count(int x) => x;
                }

                [Transpile]
                public enum Kind { A, B }
            }

            namespace App
            {
                [Transpile]
                public static class Usage
                {
                    public static int Count() => App.Mods.Counter.Count(3);

                    public static object K() => App.Mods.Kind.B;
                }
            }
            """
        )["app/usage.ts"];

        await Assert.That(output).Contains("import { count } from \"#/app/mods/counter\"");
        await Assert.That(output).Contains("import { Kind } from \"#/app/mods/kind\"");
        await Assert.That(output).Contains("return count(3);");
        await Assert.That(output).Contains("return Kind.B;");
        await Assert.That(output).DoesNotContain("@acme");
    }

    private static int OccurrencesOf(string text, string fragment) =>
        text.Split(fragment).Length - 1;

    private static string Specifier(bool withSubpathRoot) =>
        withSubpathRoot ? StrippedSpecifier : FullSpecifier;

    private static string TranspileUsage(string members, bool withSubpathRoot = false) =>
        TranspileHelper.TranspileWithLibrary(Library(withSubpathRoot), Consumer(members))[
            "app/usage.ts"
        ];

    private static string Library(bool withSubpathRoot)
    {
        var subpathRoot = withSubpathRoot ? ", SubpathRoot = \"Acme.Domain\"" : "";

        return $$"""
            using Metano.Annotations;

            [assembly: System.Reflection.AssemblyVersion("1.4.0.0")]
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@acme/domain"{{subpathRoot}})]

            namespace Acme.Domain.Episodes;

            public record EntityId(string Value);

            public enum EpisodeKind { Feature, Short }

            public record Circle(int Radius);

            public class Calc
            {
                public static readonly int Zero = 0;

                public static int Twice(int x) => x * 2;
            }

            #pragma warning disable CS0618
            [ExportedAsModule]
            #pragma warning restore CS0618
            public static class EpisodeModule
            {
                public static int Count(int x) => x;
            }

            [Branded]
            public readonly record struct UserId(string Value);

            public class Season
            {
                public enum Mode { Live, Archived }
            }
            """;
    }

    private static string Consumer(string members) =>
        $$"""
            using Acme.Domain.Episodes;

            [assembly: TranspileAssembly]

            namespace App;

            public static class Usage
            {
                {{members}}
            }
            """;
}
