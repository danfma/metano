using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Dart.Transformation;
using Metano.Compiler.Diagnostics;
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
        // Hash routes through the metano_runtime HashCode helper for cross-stack
        // determinism. Single-field records use the unsuffixed `combine`.
        await Assert.That(dart).Contains("HashCode.combine(this.amount)");
        await Assert.That(dart).DoesNotContain("Object.hash");
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
    public async Task WideRecord_UsesHashCodeBuilderInsteadOfCombine()
    {
        // The runtime HashCode helper exposes `combine`/`combine2`/`combine3`/
        // `combine4` for narrow records; anything wider must switch to the
        // builder API so every field still feeds into the hash.
        var fields = string.Join(", ", Enumerable.Range(1, 5).Select(i => $"int F{i}"));
        var (files, _) = TranspileDart(
            $$"""
            [Transpile]
            public record Wide({{fields}});
            """
        );

        var dart = files["wide.dart"];
        await Assert.That(dart).Contains("var hc = HashCode();");
        await Assert.That(dart).Contains("hc.add(this.f1);");
        await Assert.That(dart).Contains("hc.add(this.f5);");
        await Assert.That(dart).Contains("return hc.toHashCode();");
        await Assert.That(dart).DoesNotContain("HashCode.combine");
        await Assert.That(dart).DoesNotContain("Object.hash");
    }

    [Test]
    public async Task RecordType_DoesNotInjectImplicitBaseClass()
    {
        // Generated records sit directly under Dart's `Object` — same shape as
        // hand-written Dart classes. No implicit Metano base class so users can
        // mix generated records with native Dart types without a
        // `MetanoObject`-vs-anything-else split in their hierarchies.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Money(int Amount);
            """
        );

        var dart = files["money.dart"];
        await Assert.That(dart).Contains("class Money {");
        await Assert.That(dart).DoesNotContain("extends");
    }

    [Test]
    public async Task PlainClass_DoesNotInjectImplicitBaseClass()
    {
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Plain
            {
                public int X { get; }
                public Plain(int x) { X = x; }
            }
            """
        );

        var dart = files["plain.dart"];
        await Assert.That(dart).Contains("class Plain {");
        await Assert.That(dart).DoesNotContain("extends");
    }

    [Test]
    public async Task Delegate_LowersToDartTypedef()
    {
        // C# delegates lower to Dart typedefs aliasing a function signature.
        // Asserting the diagnostic CODE (not just the legacy message) catches
        // any future regression that re-introduces a different fallback warning.
        var (files, diagnostics) = TranspileDart(
            """
            [Transpile]
            public delegate void ClickHandler(int x, int y);
            """
        );

        var dart = files["click_handler.dart"];
        await Assert.That(dart).Contains("typedef ClickHandler = void Function(int, int);");
        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsFalse();
    }

    [Test]
    public async Task GenericDelegate_PreservesConstraintBound()
    {
        // The collector also walks `extends` bounds on type parameters so a
        // constraint that references a cross-package type still pulls in the
        // matching import. Local user-defined constraints round-trip through
        // the typedef header.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public abstract class Comparable<T> {}

            [Transpile]
            public delegate T Sorter<T>(T left, T right) where T : Comparable<T>;
            """
        );

        var dart = files["sorter.dart"];
        await Assert.That(dart).Contains("typedef Sorter<T extends Comparable<T>> =");
    }

    [Test]
    public async Task GenericDelegate_PreservesTypeParameters()
    {
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public delegate T Factory<T>();
            """
        );

        var dart = files["factory.dart"];
        await Assert.That(dart).Contains("typedef Factory<T> = T Function();");
    }

    [Test]
    public async Task DelegateWithThisAttribute_ReintroducesReceiverAsPositionalParam()
    {
        // Dart has no JS-style `this` rebinding, so `[This]` degrades — the
        // receiver flows through as a regular positional parameter at index 0.
        // Mirrors the existing widget-delegate behavior the class bridge already
        // documents.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public abstract class Element {}

            [Transpile]
            public delegate void MouseListener([This] Element self, int x);
            """
        );

        var dart = files["mouse_listener.dart"];
        await Assert.That(dart).Contains("typedef MouseListener = void Function(Element, int);");
    }

    [Test]
    public async Task DartMethodMapping_RewritesStaticBclCallSite()
    {
        // `[MapMethod(..., DartMethod = "...")]` on a static C# member rewrites
        // the call site to a bare Dart function call (the qualifier is dropped
        // because Dart static functions live at top level).
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Greeter
            {
                public static void Greet() => Console.WriteLine("hi");
            }
            """
        );

        var dart = files["greeter.dart"];
        await Assert.That(dart).Contains("print('hi')");
        await Assert.That(dart).DoesNotContain("Console.WriteLine");
    }

    [Test]
    public async Task DartMethodMapping_HonorsWhenArgCountFilter()
    {
        // The Console.WriteLine → print mapping is gated to single-arg
        // overloads (WhenArgCount = 1). A format-string call site stays
        // unmapped because Dart's `print` only accepts one argument.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class FormattedLogger
            {
                public static void Log() => Console.WriteLine("hi {0}", 1);
            }
            """
        );

        var dart = files["formatted_logger.dart"];
        await Assert.That(dart).DoesNotContain("print('hi {0}', 1)");
    }

    [Test]
    public async Task DartMethodMapping_OnlyAppliesToDartTarget()
    {
        // Mappings declared only on the JS side leave the Dart output untouched
        // — the Dart bridge consults DartName / DartTemplate, not JsName /
        // JsTemplate. The positive assertion guards against the rewriter ever
        // wiring up the JS-side template by mistake.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Greeter
            {
                public static string Hello() => string.Empty;
            }
            """
        );

        var dart = files["greeter.dart"];
        await Assert.That(dart).Contains("static String hello()");
        await Assert.That(dart).DoesNotContain("emptyString");
    }

    [Test]
    public async Task ExplicitBaseClass_PassesThrough()
    {
        // A record with an explicit C# base class keeps that base on the Dart
        // side untouched — the bridge only respects what the source declares.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public abstract record Animal(string Name);

            [Transpile]
            public record Dog(string Name, string Breed) : Animal(Name);
            """
        );

        var dart = files["dog.dart"];
        await Assert.That(dart).Contains("class Dog extends Animal");
    }

    [Test]
    public async Task NoContainer_LowersToTopLevelFunctions()
    {
        // A static class tagged [NoContainer] should emit top-level Dart
        // functions rather than a Dart class of static methods — the idiomatic
        // utility-module shape on the Dart side.
        var (files, _) = TranspileDart(
            """
            [Transpile, NoContainer]
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
    public async Task IgnoredDartType_DoesNotAppearInConsumerImports()
    {
        // A type marked [Ignore(TargetLanguage.Dart)] must not leak into
        // another Dart file's import list — the target file is never written,
        // so an `import 'shape.dart'` reference would fail to resolve.
        var (files, _) = TranspileDart(
            """
            [Transpile, Ignore(TargetLanguage.Dart)]
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
    public async Task NoContainer_CollectsImportsFromParameterAndReturnTypes()
    {
        // Top-level DartFunctions emitted for [NoContainer] must still
        // contribute imports for any transpiled types they reference —
        // otherwise a cross-module API call fails to analyze.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Order { public int Id { get; } public Order(int id) { Id = id; } }

            [Transpile, NoContainer]
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
    public async Task IgnorePerTarget_DropsDartFileOnly()
    {
        // [Ignore(TargetLanguage.Dart)] should skip the Dart file emission
        // while the type remains discoverable for the (hypothetical) other
        // target. In this Dart-only test we just verify the file is absent.
        var (files, _) = TranspileDart(
            """
            [Transpile, Ignore(TargetLanguage.Dart)]
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
    public async Task NoContainer_PreservesDefaultParameters()
    {
        // A [NoContainer] function with a defaulted parameter must keep
        // the Dart optional-positional shape (`[int x = 1]`) rather than
        // dropping the default and making the parameter required.
        var (files, _) = TranspileDart(
            """
            [Transpile, NoContainer]
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

    [Test]
    public async Task ThisAttribute_DartBridge_ReintroducesReceiverAsFirstParameter()
    {
        // Dart has no JS-style `this` rebind, so the `[This]`
        // attribute degrades to a no-op: the receiver promoted into
        // IrFunctionTypeRef.ThisType is re-introduced as a regular
        // positional parameter at index 0. Guards that the TS-
        // specific rewrite does not silently drop the receiver from
        // the Dart signature.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public abstract class Element {}

            public delegate void MouseEventListener([This] Element self, string arg);

            [Transpile]
            public class Widget
            {
                public MouseEventListener? OnClick { get; set; }
            }
            """
        );

        var dart = files["widget.dart"];
        // The receiver type still appears as the first positional
        // parameter in the emitted function-typed field.
        await Assert.That(dart).Contains("Element");
    }

    [Test]
    public async Task TemporalRelationalLowering_DoesNotLeakIntoDartTarget()
    {
        // The TypeScript-specific `Temporal.*.compare(...) op 0`
        // rewrite lives in the shared extractor. It must not fire for
        // the Dart target — Dart's native `DateTime` supports the raw
        // relational operators, so the generated Dart should emit
        // `a >= b` as-is and never reference the `Temporal` runtime.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Scheduler
            {
                public bool OnOrAfter(System.DateOnly a, System.DateOnly b) => a >= b;
            }
            """
        );

        var dart = files["scheduler.dart"];
        await Assert.That(dart).Contains("a >= b");
        await Assert.That(dart).DoesNotContain("Temporal");
        await Assert.That(dart).DoesNotContain("compare");
    }

    [Test]
    public async Task RecordType_EmitsMetanoRuntimeHashCodeImport()
    {
        // Records lower to a class with synthesized `==`/`hashCode`. The
        // IrRuntimeRequirementScanner reports a `HashCode` requirement for
        // every non-PlainObject record, and the Dart import collector turns
        // that into a `show HashCode` import from `metano_runtime`.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Money(int Amount);
            """
        );

        var dart = files["money.dart"];
        await Assert
            .That(dart)
            .Contains("import 'package:metano_runtime/metano_runtime.dart' show HashCode;");
    }

    [Test]
    public async Task PlainClass_DoesNotImportMetanoRuntime()
    {
        // Non-record classes don't synthesize value equality, so the scanner
        // must not surface a HashCode requirement and the file should stay
        // free of any metano_runtime import.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Plain
            {
                public int X { get; }
                public Plain(int x) { X = x; }
            }
            """
        );

        var dart = files["plain.dart"];
        await Assert.That(dart).DoesNotContain("import 'package:metano_runtime");
    }

    [Test]
    public async Task NoContainer_DoesNotInheritRecordRuntimeRequirements()
    {
        // Module-level functions emitted for [NoContainer] go through the
        // ScanFunctionsInto path, which only walks return-type / parameter type
        // references. The record's HashCode requirement belongs to the record's
        // OWN file, not to consumers — so the Treasury module must stay free of
        // the `metano_runtime` import even though it returns a `Money` record.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public record Money(int Amount);

            [Transpile, NoContainer]
            public static class Treasury
            {
                public static Money Empty() => new Money(0);
            }
            """
        );

        var module = files["treasury.dart"];
        await Assert.That(module).DoesNotContain("import 'package:metano_runtime");
        // The relative import to the record's file is still emitted — that is
        // how the consumer reaches Money's transitively-correct hashCode.
        await Assert.That(module).Contains("import 'money.dart';");
        // And the record file itself carries the HashCode import.
        await Assert
            .That(files["money.dart"])
            .Contains("import 'package:metano_runtime/metano_runtime.dart' show HashCode;");
    }

    [Test]
    public async Task PlainObjectRecord_DoesNotImportMetanoRuntime()
    {
        // [PlainObject] records emit as data carriers without value-equality
        // synthesis, so the scanner must skip the HashCode requirement.
        var (files, _) = TranspileDart(
            """
            [Transpile, PlainObject]
            public record Dto(int Id, string Name);
            """
        );

        var dart = files["dto.dart"];
        await Assert.That(dart).DoesNotContain("import 'package:metano_runtime");
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
        var printer = new Metano.Compiler.Dart.Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }
}
