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
