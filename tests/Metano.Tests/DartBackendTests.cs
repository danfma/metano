using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Dart.Transformation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Regression tests for the Dart backend. Each test drives the real
/// <see cref="DartTransformer"/> end-to-end (Roslyn compile → IR extract →
/// Dart bridge → Dart printer) over a minimal inline snippet and pins the
/// generated Dart source so we catch drift.
/// </summary>
public class DartBackendTests
{
    [Test]
    public async Task DuplicateSimpleTypeNames_ReportDiagnosticInsteadOfCrashing()
    {
        // Admin.User and Billing.User share the simple name "User". TransformAll
        // previously crashed on Dictionary key collision; now it should complete
        // with a MetanoDiagnostic surfaced for the ambiguity.
        var (files, diagnostics) = TranspileDart(
            """
            namespace Admin {
                [Transpile]
                public class User { }
            }
            namespace Billing {
                [Transpile]
                public class User { }
            }
            """
        );

        await Assert.That(files.Count).IsEqualTo(1);
        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.AmbiguousConstruct))
            .IsTrue();
    }

    [Test]
    public async Task DefaultParameterValue_RendersAsOptionalPositional()
    {
        // The ctor's parameters are auto-promoted to `this.name`/`this.age` because
        // the properties share their names (Dart's field-initializer shorthand),
        // and the default expression lands inside the [...] optional block.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class UserDto
            {
                public UserDto(string name, int age = 0)
                {
                    Name = name;
                    Age = age;
                }
                public string Name { get; }
                public int Age { get; }
            }
            """
        );

        var dart = files["user_dto.dart"];
        await Assert.That(dart).Contains("UserDto(this.name, [this.age = 0])");
    }

    [Test]
    public async Task DefaultParameterValue_OnRegularParameter_RendersOptionalBracket()
    {
        // Non-promoted parameter — no matching property — keeps its type and
        // renders inside the optional block with the default expression.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Scaler
            {
                public int Factor { get; private set; }
                public Scaler(int factor, int boost = 1)
                {
                    Factor = factor * boost;
                }
            }
            """
        );

        var dart = files["scaler.dart"];
        await Assert.That(dart).Contains("[int boost = 1]");
    }

    [Test]
    public async Task UserDefinedOperator_RendersAsDartOperatorSyntax()
    {
        // A C# `operator +` on a Money record should become `Money operator +(Money other)`
        // in Dart rather than a method named `op_Addition` or similar.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Money(int Amount)
            {
                public static Money operator +(Money a, Money b) => new Money(a.Amount + b.Amount);
            }
            """
        );

        var dart = files["money.dart"];
        await Assert.That(dart).Contains("operator +");
    }

    [Test]
    public async Task RecordType_SynthesizesEqualsHashCodeAndCopyWith()
    {
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Money(int Amount);
            """
        );

        var dart = files["money.dart"];
        // Value equality: narrow to Money via `is`, enforce exact runtimeType
        // match (so Base == Derived stays false — C# record semantics + Dart
        // `==` contract symmetry), then compare each field.
        await Assert.That(dart).Contains("operator ==");
        await Assert.That(dart).Contains("other is Money && other.runtimeType == this.runtimeType");
        await Assert.That(dart).Contains("other.amount == this.amount");
        // Hash combines all fields using Dart's built-in Object.hash helper.
        await Assert.That(dart).Contains("Object.hash(this.amount)");
        // copyWith takes every field as an optional named parameter — each is
        // nullable so callers can omit it, and the body falls back to the
        // current instance value via `??`.
        await Assert.That(dart).Contains("Money copyWith({int? amount})");
        await Assert.That(dart).Contains("amount ?? this.amount");
        // `==` and `hashCode` override Object — emit @override to silence the
        // analyzer.
        await Assert.That(dart).Contains("@override");
    }

    [Test]
    public async Task WideRecord_UsesObjectHashAllInsteadOfObjectHash()
    {
        // Object.hash in Dart caps at 20 positional arguments. A record wider
        // than that must switch to Object.hashAll(Iterable) or the generated
        // code fails to compile.
        var fields = string.Join(", ", Enumerable.Range(1, 21).Select(i => $"int F{i}"));
        var (files, _) = TranspileDart(
            $$"""
            [Transpile]
            public record Wide({{fields}});
            """
        );

        var dart = files["wide.dart"];
        await Assert.That(dart).Contains("Object.hashAll([");
        await Assert.That(dart).DoesNotContain("Object.hash(this.f1,");
    }

    [Test]
    public async Task ExportedAsModule_LowersToTopLevelFunctions()
    {
        // A static class tagged [ExportedAsModule] should emit top-level Dart
        // functions rather than a Dart class of static methods — the idiomatic
        // utility-module shape on the Dart side.
        var (files, _) = TranspileDart(
            """
            [Transpile, ExportedAsModule]
            public static class MathUtils
            {
                public static int Double(int x) => x * 2;
            }
            """
        );

        var dart = files["math_utils.dart"];
        await Assert.That(dart).Contains("int double(int x)");
        await Assert.That(dart).DoesNotContain("class MathUtils");
    }

    [Test]
    public async Task TargetSpecificNameOverride_WinsOverUntargetedName()
    {
        // Multiple [Name] attributes coexist on the same symbol: the Dart-
        // specific one wins for the Dart target, the untargeted one would
        // have applied if no per-target override matched.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            [Name("Counter")]
            [Name(TargetLanguage.Dart, "CounterDto")]
            public class Counter
            {
                public int Count { get; }
                public Counter(int count) { Count = count; }
            }
            """
        );

        // File and class name both reflect the Dart-specific rename.
        await Assert.That(files.Keys).Contains("counter_dto.dart");
        var dart = files["counter_dto.dart"];
        await Assert.That(dart).Contains("class CounterDto");
    }

    [Test]
    public async Task UntargetedNameOverride_AppliesWhenNoPerTargetMatches()
    {
        // Only an untargeted [Name] — applies to every target that lacks a
        // per-target override, so Dart picks it up even though the attribute
        // isn't Dart-specific.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            [Name("MyCounter")]
            public class Counter
            {
                public int Count { get; }
                public Counter(int count) { Count = count; }
            }
            """
        );

        await Assert.That(files.Keys).Contains("my_counter.dart");
        await Assert.That(files["my_counter.dart"]).Contains("class MyCounter");
    }

    [Test]
    public async Task NoEmitDartType_DoesNotAppearInConsumerImports()
    {
        // A type marked [NoEmit(TargetLanguage.Dart)] must not leak into
        // another Dart file's import list — the target file is never written,
        // so an `import 'shape.dart'` reference would fail to resolve.
        var (files, _) = TranspileDart(
            """
            [Transpile, NoEmit(TargetLanguage.Dart)]
            public interface IShape
            {
                int Area();
            }

            [Transpile]
            public class Circle
            {
                public IShape Shape { get; }
                public Circle(IShape shape) { Shape = shape; }
            }
            """
        );

        await Assert.That(files.Keys).DoesNotContain("i_shape.dart");
        await Assert.That(files["circle.dart"]).DoesNotContain("import 'i_shape.dart'");
    }

    [Test]
    public async Task ExportedAsModule_CollectsImportsFromParameterAndReturnTypes()
    {
        // Top-level DartFunctions emitted for [ExportedAsModule] must still
        // contribute imports for any transpiled types they reference —
        // otherwise a cross-module API call fails to analyze.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Order { public int Id { get; } public Order(int id) { Id = id; } }

            [Transpile, ExportedAsModule]
            public static class OrderRepository
            {
                public static Order FindById(int id) => new Order(id);
            }
            """
        );

        var module = files["order_repository.dart"];
        await Assert.That(module).Contains("import 'order.dart';");
        await Assert.That(module).Contains("Order findById(int id)");
    }

    [Test]
    public async Task OverloadedMethods_CollapseIntoSingleDartEmissionPlusDiagnostic()
    {
        // Roslyn exposes each overload as a separate IMethodSymbol; the IR
        // extractor must fold them into one primary with Overloads populated
        // so DartTransformer emits a single method declaration and the Dart
        // "no overloading" diagnostic fires.
        var (files, diagnostics) = TranspileDart(
            """
            [Transpile]
            public class Widget
            {
                public void Draw() { }
                public void Draw(int times) { }
            }
            """
        );

        var dart = files["widget.dart"];
        // Only ONE `draw` declaration — no duplicate emission.
        var drawCount = (dart.Split("void draw(").Length - 1) + (dart.Split("draw();").Length - 1);
        await Assert.That(drawCount).IsEqualTo(1);
        await Assert
            .That(
                diagnostics.Any(d => d.Message.Contains("Dart doesn't support method overloading"))
            )
            .IsTrue();
    }

    [Test]
    public async Task AutoPropertyWithInitializer_CarriesInitializerIntoDartField()
    {
        // A C# auto-property with `= initializer` must carry the initializer
        // through to the Dart field; without it the field would either change
        // runtime meaning or need an unjustified `late` modifier.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Counter
            {
                public int Count { get; } = 42;
            }
            """
        );

        var dart = files["counter.dart"];
        await Assert.That(dart).Contains("final int count = 42;");
        await Assert.That(dart).DoesNotContain("late int count;");
    }

    [Test]
    public async Task TargetSpecificIgnore_DropsMemberOnlyForTarget()
    {
        // [Ignore(TargetLanguage.Dart)] on a method should remove it from the
        // Dart output. The TS target (tested separately) must still emit it.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Widget
            {
                [Ignore(TargetLanguage.Dart)]
                public void DartUnfriendly() { }

                public void Regular() { }
            }
            """
        );

        var dart = files["widget.dart"];
        await Assert.That(dart).DoesNotContain("dartUnfriendly");
        await Assert.That(dart).Contains("regular()");
    }

    [Test]
    public async Task TargetSpecificNoEmit_DropsTypeFileOnlyForTarget()
    {
        // [NoEmit(TargetLanguage.Dart)] should skip the Dart file emission
        // while the type remains discoverable for the (hypothetical) other
        // target. In this Dart-only test we just verify the file is absent.
        var (files, _) = TranspileDart(
            """
            [Transpile, NoEmit(TargetLanguage.Dart)]
            public class AmbientShape
            {
                public int X { get; }
                public AmbientShape(int x) { X = x; }
            }

            [Transpile]
            public class Other { }
            """
        );

        await Assert.That(files.Keys).DoesNotContain("ambient_shape.dart");
        await Assert.That(files.Keys).Contains("other.dart");
    }

    [Test]
    public async Task ExportedAsModule_PreservesDefaultParameters()
    {
        // A [ExportedAsModule] function with a defaulted parameter must keep
        // the Dart optional-positional shape (`[int x = 1]`) rather than
        // dropping the default and making the parameter required.
        var (files, _) = TranspileDart(
            """
            [Transpile, ExportedAsModule]
            public static class Calc
            {
                public static int Inc(int x, int step = 1) => x + step;
            }
            """
        );

        var dart = files["calc.dart"];
        await Assert.That(dart).Contains("[int step = 1]");
    }

    [Test]
    public async Task NamedArgument_RendersWithDartNamedArgumentSyntax()
    {
        // When C# passes `new Widget(Width: 2, Height: 3)` the Dart backend
        // should render it as `Widget(width: 2, height: 3)` — keeping the
        // named-arg intent the source expressed rather than collapsing to
        // positional order.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Widget(int Width, int Height)
            {
                public static Widget Square(int size) => new Widget(Width: size, Height: size);
            }
            """
        );

        var dart = files["widget.dart"];
        await Assert.That(dart).Contains("Widget(width: size, height: size)");
    }

    [Test]
    public async Task WithExpression_LowersToCopyWithOnDartSide()
    {
        // C#'s `record with { X = e }` has no Dart equivalent — the bridge
        // reuses the synthesized copyWith method, keeping named parameters
        // so the call reads naturally on the Dart side.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Point(int X, int Y)
            {
                public Point ShiftX(int dx) => this with { X = X + dx };
            }
            """
        );

        var dart = files["point.dart"];
        await Assert.That(dart).Contains("this.copyWith(x: this.x + dx)");
    }

    [Test]
    public async Task SwitchExpression_RendersAsDartSwitchExpression()
    {
        // Dart 3 has native switch expressions with the same first-match shape as
        // C#. Each arm lowers to `pattern => result`, keeping the scrutinee in a
        // single bound form instead of the TS-side IIFE workaround.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Classifier
            {
                public string Describe(int n) => n switch { 0 => "zero", _ => "many" };
            }
            """
        );

        var dart = files["classifier.dart"];
        await Assert.That(dart).Contains("switch (n)");
        // Dart string literals render with single quotes by the IR body printer.
        await Assert.That(dart).Contains("0 => 'zero'");
        await Assert.That(dart).Contains("_ => 'many'");
    }

    [Test]
    public async Task UninitializedStaticNonNullableField_EmitsLate()
    {
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public static class Counters
            {
                public static int Count;
            }
            """
        );

        var dart = files["counters.dart"];
        await Assert.That(dart).Contains("static late int count;");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileDart(string csharpSource)
    {
        var source = $"""
            using System;
            using Metano.Annotations;
            {csharpSource}
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );
        // Share the same cached metadata references that TranspileHelper uses —
        // rebuilding the ~200-entry list per test would add hundreds of MB of
        // churn across the suite.
        var compilation = CSharpCompilation.Create(
            "DartTestAssembly",
            [syntaxTree],
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
        var printer = new Metano.Dart.Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }
}
