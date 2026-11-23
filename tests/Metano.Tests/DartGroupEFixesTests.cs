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

        await DartToolchain.AssertAnalyzesAsync(files);
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

    [Test]
    public async Task RecordEquality_UsesEqualityMembersAndChainsToRecordBase_243()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record BaseRec(int A);

            [Transpile]
            public record DerivedRec(int A, int B) : BaseRec(A)
            {
                public int Extra { get; init; }
            }

            [Transpile]
            public static class Matcher
            {
                public static bool IsOrigin(BaseRec r) => r switch
                {
                    BaseRec(0) => true,
                    _ => false,
                };
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var derived = files["derived_rec.dart"];
        var matcher = files["matcher.dart"];

        await Assert.That(derived).Contains("super == other");
        await Assert.That(derived).Contains("other.extra == this.extra");
        await Assert.That(derived).Contains("super.hashCode");
        await Assert.That(derived).Contains("this.extra");
        await Assert.That(matcher).Contains("BaseRec(a: 0)");
    }

    [Test]
    public async Task CollectionBaseType_ReportsMs0001_AndOmitsExtends_FollowUp()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class MyDict : Dictionary<string, int> { }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsTrue();
        await Assert.That(files["my_dict.dart"]).DoesNotContain("extends Map");
        await DartToolchain.AssertAnalyzesAsync(files);
    }

    [Test]
    public async Task BaseConstructorCall_ReorderedNamedArgs_ReportsMs0001_572()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Tk
            {
                public static int Tr(int n) => n;
            }

            [Transpile]
            public class BaseCtor
            {
                public int V;
                public BaseCtor(int a, int b) => V = a * 10 + b;
            }

            [Transpile]
            public class DerivedCtor : BaseCtor
            {
                public DerivedCtor() : base(b: Tk.Tr(2), a: Tk.Tr(1)) { }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsTrue();
    }

    [Test]
    public async Task BaseConstructorCall_OpaqueDefault_ReportsMs0001_572()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class BaseOpt
            {
                public ulong? A;
                public int B;
                public BaseOpt(ulong? a = 10, int b = 0)
                {
                    A = a;
                    B = b;
                }
            }

            [Transpile]
            public class DerivedOpt : BaseOpt
            {
                public DerivedOpt() : base(b: 1) { }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsTrue();
    }

    [Test]
    public async Task ThisConstructorChain_OpaqueDefault_ReportsMs0001_572()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class ChainOpt
            {
                public System.DateTime Dt;
                public int B;
                public ChainOpt(System.DateTime dt = default, int b = 0)
                {
                    Dt = dt;
                    B = b;
                }

                public ChainOpt(string tag, int b) : this(b: b) { }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsTrue();
    }

    [Test]
    public async Task SuperConstructorCall_ExecutesInDart_572()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class BaseItem
            {
                public int Value;
                public BaseItem(int x) => Value = x;
                public BaseItem(string label) => Value = label.Length;
            }

            [Transpile]
            public class DerivedItem : BaseItem
            {
                public DerivedItem(int x) : base(x) { }
                public DerivedItem(string label) : base(label) { }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        var output = await DartToolchain.AnalyzeAndRunAsync(
            files,
            """
            import 'derived_item.dart';

            void main() {
              var a = DerivedItem.fromX(7);
              var b = DerivedItem.fromLabel('hello');
              if (a.value != 7 || b.value != 5) {
                throw StateError('Unexpected values: ${a.value}, ${b.value}');
              }
              print('OK');
            }
            """
        );
        await Assert.That(output).IsEqualTo("OK");
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
