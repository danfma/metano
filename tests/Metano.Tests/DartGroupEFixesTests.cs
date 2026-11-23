using System.Diagnostics;
using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Dart.Transformation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Regression tests for Wave 6 Group E (Dart classes, modules, determinism):
/// #576, #689, #410, #347, #327, #243, #572, and Dictionary inheritance.
/// </summary>
public class DartGroupEFixesTests
{
    [Test]
    public async Task JsonSerializerContext_MemberOrderIsDeterministicAcrossTreeOrders_576_689()
    {
        var partA = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            namespace Demo;

            [Transpile]
            public partial class AppJsonContext : JsonSerializerContext
            {
                public AppJsonContext() : base(null) { }
                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                private static int _alpha;
                public int Alpha => _alpha;
                public void CreateAlpha() { }
            }
            """;

        var partB = """
            namespace Demo;

            public partial class AppJsonContext
            {
                private static int _beta;
                public int Beta => _beta;
                public void CreateBeta() { }
            }
            """;

        var first = TranspileTrees([partA, partB]);
        var second = TranspileTrees([partB, partA]);

        await Assert
            .That(first["app_json_context.dart"])
            .IsEqualTo(second["app_json_context.dart"]);
    }

    [Test]
    public async Task NameOverride_AppliedConsistentlyToFieldPropertyAndUses_347_410()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record Holder(int X)
            {
                [Name("renamedGet")]
                public Func<int> Other { get; } = () => X;

                [Name("total")]
                public int Sum = 1;
            }

            [Transpile]
            public static class Consumer
            {
                public static int Use(Holder holder)
                {
                    var g = holder.Other();
                    var z = holder.Other;
                    return g + z() + holder.Sum;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var holder = files["holder.dart"];
        var consumer = files["consumer.dart"];

        await Assert.That(holder).Contains("renamedGet");
        await Assert.That(holder).Contains("late int total;");
        await Assert.That(holder).Contains("this.renamedGet = () => x;");
        await Assert.That(holder).Contains("this.total = 1;");
        await Assert.That(holder).DoesNotContain("this.other");
        await Assert.That(holder).DoesNotContain("this.sum");

        await Assert.That(consumer).Contains("holder.renamedGet()");
        await Assert.That(consumer).Contains("holder.renamedGet;");
        await Assert.That(consumer).Contains("holder.total");
        await Assert.That(consumer).DoesNotContain("holder.other");
        await Assert.That(consumer).DoesNotContain("holder.sum");
    }

    [Test]
    public async Task ModuleVariableValidator_RecordWithPrimaryConstructor_ReportsOnce_410()
    {
        var result = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Config
            {
                public static int Count = 1;
            }

            [Transpile]
            public record Reader(int X)
            {
                public int Read(int count) => Config.Count + count + X;
            }
            """
        );

        var ms0001 = result.Diagnostics.Where(d => d.Code == "MS0001").ToList();
        await Assert.That(ms0001.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ContainerlessModuleVariables_EmitTopLevelDeclarations_AndAnalyze_327()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Config
            {
                public static readonly int Max = 10;
                public static int Count = 0;
                private static int Secret = 42;
                public static int DoubleSecret() => Secret * 2;
                public static int Next() => ++Count;
            }

            [Transpile]
            public static class Consumer
            {
                public static int Read() => Config.Count + Config.Max;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var config = files["config.dart"];
        var consumer = files["consumer.dart"];

        await Assert.That(config).Contains("final int max = 10;");
        await Assert.That(config).Contains("int count = 0;");
        await Assert.That(config).Contains("int _secret = 42;");
        await Assert.That(config).Contains("_secret * 2");

        await Assert.That(consumer).Contains("import 'config.dart';");
        await Assert.That(consumer).Contains("=> count + max;");

        AssertDartAnalyzes(files);
    }

    [Test]
    public async Task ContainerlessModuleVariables_StaticCtorAndShadowing_ReportDiagnostics_327()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Config
            {
                public static int Count = 1;

                static Config()
                {
                    Count = 2;
                }

                public static int ReadShadowed(int count) => Count + count;
            }
            """
        );

        var ms0001 = diagnostics.Where(d => d.Code == "MS0001").ToList();
        await Assert.That(ms0001.Count).IsEqualTo(2);
    }

    private static void AssertDartAnalyzes(Dictionary<string, string> files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"metano-dart-w6e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var (name, content) in files)
                File.WriteAllText(Path.Combine(tempDir, name), content);

            var psi = new ProcessStartInfo("dart", "analyze")
            {
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"dart analyze failed:\n{stdout}{stderr}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Dictionary<string, string> TranspileTrees(IReadOnlyList<string> sources)
    {
        var trees = sources
            .Select(s =>
                CSharpSyntaxTree.ParseText(
                    $"using Metano.Annotations;\n{s}",
                    new CSharpParseOptions(LanguageVersion.Preview)
                )
            )
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "DartJsonDeterminismTest",
            trees,
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "C# compilation failed:\n" + string.Join("\n", errors.Select(e => e.ToString()))
            );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            TargetLanguage.Dart
        );

        var transformer = new DartTransformer(ir, compilation);
        var files = transformer.TransformAll();
        var printer = new Metano.Compiler.Dart.Printer();

        return files.ToDictionary(f => f.FileName, f => printer.Print(f));
    }
}
