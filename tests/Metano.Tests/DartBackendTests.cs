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
        var (files, diagnostics) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
    public async Task PositionalRecordProperty_OverridingAbstractProperty_IsAssignedByTheConstructor()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record Entry { public abstract int Id { get; init; } }

            [Transpile]
            public record Note(int Id) : Entry;
            """
        );

        var dart = files["note.dart"];
        await Assert.That(dart).Contains("Note(this.id);");
        await Assert.That(dart).Contains("final int id;");
        await Assert.That(dart).DoesNotContain("late final int id;");
    }

    [Test]
    public async Task RecordType_SynthesizesEqualsHashCodeAndCopyWith()
    {
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record Money(int Amount);
            """
        );

        var dart = files["money.dart"];
        await Assert.That(dart).Contains("class Money {");
        await Assert.That(dart).DoesNotContain("class Money extends");
    }

    [Test]
    public async Task PlainClass_DoesNotInjectImplicitBaseClass()
    {
        var (files, _) = DartTranspileHelper.Transpile(
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
        await Assert.That(dart).DoesNotContain("class Plain extends");
    }

    [Test]
    public async Task ExceptionWithoutConstructors_EmitsHarmlessExplicitConstructor()
    {
        // The core models the implicit constructor of a BCL-derived exception
        // (its base call is observable on the TS side); Dart renders it as an
        // empty constructor, equivalent to the implicit one.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class NoConstructorException : System.Exception { }
            """
        );

        var dart = files["no_constructor_exception.dart"];

        await Assert.That(dart).Contains("class NoConstructorException extends Exception {");
        await Assert.That(dart).Contains("NoConstructorException();");
    }

    [Test]
    public async Task Delegate_LowersToDartTypedef()
    {
        // C# delegates lower to Dart typedefs aliasing a function signature.
        // Asserting the diagnostic CODE (not just the legacy message) catches
        // any future regression that re-introduces a different fallback warning.
        var (files, diagnostics) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, diagnostics) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
    public async Task AsCast_LowersToNativeTypeTest()
    {
        // `x as T` lowers to `x is T ? (T)x : null` in the shared extractor.
        // Dart reifies interfaces, so the guard-or-MS0001 rule TypeScript
        // needs for an erased interface does not apply here.
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile] public interface IShape { double Area(); }
            [Transpile] public class Square { }
            [Transpile]
            public class Caster
            {
                public Square? AsSquare(object o) => o as Square;
                public IShape? AsShape(object o) => o as IShape;
            }
            """
        );

        var dart = files["caster.dart"];
        await Assert.That(dart).Contains("o is Square ? o as Square : null");
        await Assert.That(dart).Contains("o is IShape ? o as IShape : null");
        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task RecordType_EmitsMetanoRuntimeHashCodeImport()
    {
        // Records lower to a class with synthesized `==`/`hashCode`. The
        // IrRuntimeRequirementScanner reports a `HashCode` requirement for
        // every non-PlainObject record, and the Dart import collector turns
        // that into a `show HashCode` import from `metano_runtime`.
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
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
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Dto(int Id, string Name);
            """
        );

        var dart = files["dto.dart"];
        await Assert.That(dart).DoesNotContain("import 'package:metano_runtime");
    }

    [Test]
    public async Task NestedRecordInNonRecordParent_DoesNotFoldChildHashCodeIntoParentFile()
    {
        // Regression guard for feature 006: runtime requirements are collected per emitted
        // file. Dart emits one file per type, so a nested record's HashCode belongs to the
        // child's file, never the parent's. An earlier TypeScript fix that populated the
        // shared core IR's NestedTypes folded the child's requirement into the parent and
        // regressed exactly this — the collection must stay a per-target, per-file concern.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Outer
            {
                public sealed record Inner(int Count);
            }
            """
        );

        await Assert.That(files["inner.dart"]).Contains("HashCode");
        await Assert.That(files["outer.dart"]).DoesNotContain("HashCode");
    }

    [Test]
    public async Task BinaryOperands_KeepSourceGrouping()
    {
        // The IR keeps grouping only as tree shape; the printer must restore the
        // parentheses Dart precedence would otherwise drop (#263: `x[^(n + 1)]`
        // lowers to `x[x.length - (n + 1)]`).
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Operands
            {
                public int Sub(int a, int b, int c) => a - (b + c);
                public int Scale(int a, int b, int c) => (a + b) * c;
                public int Chain(int a, int b, int c) => a - b - c;
                public bool Both(bool a, bool b, bool c) => a && (b && c);
                public bool Same(bool a, bool b, bool c) => (a == b) == c;
            }
            """
        );

        var dart = files["operands.dart"];
        await Assert.That(dart).Contains("a - (b + c)");
        await Assert.That(dart).Contains("(a + b) * c");
        await Assert.That(dart).Contains("a - b - c");
        await Assert.That(dart).Contains("a && b && c");
        await Assert.That(dart).Contains("(a == b) == c");
    }

    [Test]
    public async Task OperatorReceivers_KeepSourceGrouping()
    {
        // A receiver that binds looser than a selector must keep its parentheses:
        // `(value ?? "").Trim()` printed bare is `value ?? ''.trim()` (#324).
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Receivers
            {
                public string Trimmed(string? value) => (value ?? "").Trim();
                public int Length(string? a, string b) => (a ?? b).Length;
                public string Negated(int a) => (-a).ToString();
                public int Cast(object o) => ((string)o).Length;
            }
            """
        );

        var dart = files["receivers.dart"];

        await Assert.That(dart).Contains("(value ?? '').trim()");
        await Assert.That(dart).Contains("(a ?? b).length");
        await Assert.That(dart).Contains("(-a).toString()");
        await Assert.That(dart).Contains("(o as String).length");
    }

    [Test]
    public async Task DictionaryCounts_MapToLength()
    {
        // #295 rebinds interface dictionary counts and folds view counts into the
        // dictionary's own Count, so the dictionary entries carry the Dart `length`.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Counts
            {
                public int Concrete(Dictionary<string, int> d) => d.Count;
                public int Mutable(IDictionary<string, int> d) => d.Count;
                public int ReadOnly(IReadOnlyDictionary<string, int> d) => d.Count;
                public int Keys(Dictionary<string, int> d) => d.Keys.Count;
                public int Values(IDictionary<string, int> d) => d.Values.Count;
                public int Held(Dictionary<string, int> d)
                {
                    var keys = d.Keys;
                    return keys.Count;
                }
            }
            """
        );

        var dart = files["counts.dart"];
        await Assert.That(dart).Contains("int concrete(Map<String, int> d) => d.length;");
        await Assert.That(dart).Contains("int mutable(Map<String, int> d) => d.length;");
        await Assert.That(dart).Contains("int readOnly(Map<String, int> d) => d.length;");
        await Assert.That(dart).Contains("int keys(Map<String, int> d) => d.length;");
        await Assert.That(dart).Contains("int values(Map<String, int> d) => d.length;");
        await Assert.That(dart).Contains("return keys.length;");
    }

    [Test]
    public async Task LiftedNullableOperators_GuardTheNullOperand()
    {
        // The lifted-operator lowering is target-agnostic IR (#287): Dart
        // rejects `int? + int` and `null < 1` alike, so the guard is
        // required there too.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            using System.Collections.Generic;

            [Transpile]
            public class Lifted
            {
                public List<int> Items { get; } = [];
                public Lifted? Next { get; set; }
                public int? Qty { get; set; }
                public int? Add(int? a, int k) => a + k;
                public int? OrElse(int? a, int? b) => (a + 1) ?? b;
                public int NegatedOrZero(int? a) => -a ?? 0;
                #pragma warning disable CS8629
                public int Unchecked(int? a) => a.Value;
                #pragma warning restore CS8629
                public int? NestedPlus(Lifted? o) => o?.Next?.Qty + 1;
                public string Size(int? n) => n switch { > 5 => "big", _ => "small" };
                public bool IsInt(int? a) => (a + 1) is int;
                public int Twice(int a) => -(-a);
                public int Pick(bool c, int a, int b) => c ? a : b;
                public int? Assigned(int? a, int b)
                {
                    int? x = null;
                    return a ?? (x = b);
                }
                public bool Less(int? a) => a < 5;
                public bool Has(int? a) => a.HasValue;
                public int OrZero(int? a) => a.GetValueOrDefault();
                public int Sum(Lifted? o) => o?.Items.Count + 1 ?? 0;
            }
            """
        );

        var dart = files["lifted.dart"];

        await Assert.That(dart).Contains("int? add(int? a, int k) => a == null ? null : a + k;");
        await Assert.That(dart).Contains("bool less(int? a) => a != null && a < 5;");
        await Assert.That(dart).Contains("bool has(int? a) => a != null;");
        await Assert.That(dart).Contains("int orZero(int? a) => a ?? 0;");

        // A conditional or `throw` operand binds looser than any operator.
        await Assert
            .That(dart)
            .Contains("int? orElse(int? a, int? b) => (a == null ? null : a + 1) ?? b;");
        await Assert
            .That(dart)
            .Contains("int negatedOrZero(int? a) => (a == null ? null : -a) ?? 0;");
        await Assert.That(dart).Contains("int unchecked(int? a) => a ?? (throw ");

        await Assert
            .That(dart)
            .Contains("bool isInt(int? a) => (a == null ? null : a + 1) is int;");
        await Assert.That(dart).Contains("int twice(int a) => -(-a);");

        // A nullable relational pattern's implicit non-null test is Dart's
        // null-check pattern (#256).
        await Assert.That(dart).Contains("_? && > 5");
        await Assert.That(dart).DoesNotContain("Dart lacks `not` patterns");
        await Assert.That(dart).Contains("int pick(bool c, int a, int b) => c ? a : b;");
        await Assert.That(dart).Contains("return a ?? (x = b);");

        // A nested access is the guard's argument, printed outside the guard
        // closure (here as a native null-shorting chain).
        await Assert
            .That(dart)
            .Contains(
                "int? nestedPlus(Lifted? o) => (($receiver) => $receiver == null ? null : $receiver.qty == null ? null : $receiver.qty + 1)(o?.next);"
            );

        // The operator moved into the null-conditional guard prints as a
        // guarded closure, not as an unsupported placeholder.
        await Assert
            .That(dart)
            .Contains(
                "int sum(Lifted? o) => (($receiver) => $receiver == null ? null : $receiver.items.length + 1)(o) ?? 0;"
            );
    }

    [Test]
    public async Task NullConditionalAccess_PrintsNativeNullShorting()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable
            using System.Collections.Generic;

            [Transpile]
            public class Order
            {
                public List<int> Items { get; } = [];
                public Order? Next { get; set; }

                public int? Count(Order? order) => order?.Items.Count;
                public int? First(List<int>? items) => items?[0];
                public int? NextCount(Order? order) => order?.Next?.Items.Count;
                public int? Last(Order? order) => order?.Items[^1];
            }
            """
        );

        var dart = files["order.dart"];

        await Assert.That(dart).Contains("order?.items.length");
        await Assert.That(dart).Contains("items?[0]");
        await Assert.That(dart).Contains("order?.next?.items.length");

        // A receiver read twice (`^1`) cannot null-short natively, so it is
        // bound once and guarded.
        await Assert
            .That(dart)
            .Contains(
                "int? last(Order? order) => (($receiver) => $receiver == null ? null : $receiver.items[$receiver.items.length - 1])(order);"
            );

        await Assert.That(dart).DoesNotContain("TODO");
    }

    [Test]
    public async Task StaticClassWithNonPublicNestedTypes_TranspilesWithoutCrashing()
    {
        // #264 widened assembly-wide transpilability to nested types of any
        // accessibility. The Dart prototype keeps its own public-only nested
        // filter (non-public nested types are a known Dart gap: `ShotEntry` is not
        // declared); the shared IR extraction (qualified nested references) must
        // not break it.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class ShotSchedule
            {
                private sealed record ShotEntry(string Name, int Frames);

                public enum Kind { A, B }

                public static int Total(string name)
                {
                    var entry = new ShotEntry(name, 3);
                    var kind = Kind.B;
                    return entry.Frames + (kind == Kind.B ? 1 : 0);
                }
            }
            """
        );

        await Assert.That(files.ContainsKey("shot_schedule.dart")).IsTrue();
        // Value-position type references print only the innermost segment: the Dart
        // prototype flattens nested types instead of emitting companion namespaces.
        await Assert.That(files["shot_schedule.dart"]).Contains("var kind = Kind.b;");
        await Assert.That(files.Keys).Contains("kind.dart");
    }

    [Test]
    public async Task IntegerDivision_InTheDartTarget_UsesTheTruncatingOperator()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Quotients
            {
                public static int Average(int total, int count) => total / count;

                public static double Real(double a, double b) => a / b;

                public static int Shrink(int value)
                {
                    value /= 3;
                    return value;
                }
            }
            """
        );

        var dart = files["quotients.dart"];

        await Assert.That(dart).Contains("total ~/ count");
        await Assert.That(dart).Contains("value = value ~/ 3;");
        await Assert.That(dart).Contains("a / b");
    }

    [Test]
    public async Task ValueEqualityAndTuples_InTheDartTarget_UseNativeOperatorsAndRecords()
    {
        // Record `==` is value equality in Dart too (the emitted class defines
        // `operator ==`), so it keeps the native operator, and a tuple literal
        // is a Dart record literal.
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record Point(int X, int Y);

            [Transpile]
            public class Comparisons(int seed)
            {
                public int Twice { get; } = seed * 2;

                public bool Same(Point a, Point b) => a == b;

                public bool Differ(Point a, Point b) => a != b;

                public bool SameTuple((int, int) t) => t == (1, 2);

                public (int, int) Pair() => (seed, 2);

                public int Hash(Point a) => a.GetHashCode();
            }
            """
        );

        var dart = files["comparisons.dart"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(dart).Contains("a == b");
        await Assert.That(dart).Contains("a != b");
        await Assert.That(dart).Contains("(this._seed, 2)");
        await Assert.That(dart).Contains("t.$1 == 1 && t.$2 == 2");
        await Assert.That(dart).Contains("a.hashCode");
    }

    [Test]
    public async Task ObjectContractOverrides_InTheDartTarget_BecomeHashCodeGetterAndEqualityOperator()
    {
        // A `GetHashCode` override is Dart's `hashCode` getter, the member
        // every `x.GetHashCode()` call reads; an `Equals(object)` override
        // stays a method and gains an `operator ==` that delegates to it.
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public class Money
            {
                public int Cents;

                public override bool Equals(object? other) => other is Money m && m.Cents == Cents;

                public override int GetHashCode() => Cents;

                public int Hash(Money m) => m.GetHashCode();
            }
            """
        );

        var dart = files["money.dart"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(dart).Contains("@override\n  int get hashCode => this.cents;");
        await Assert.That(dart).DoesNotContain("getHashCode");
        await Assert.That(dart).Contains("bool operator ==(Object other) => this.equals(other);");
        await Assert.That(dart).Contains("m.hashCode");
    }

    [Test]
    public async Task RecordDeclaringItsOwnEquality_InTheDartTarget_KeepsTheUserMembers()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public record Point(int X, int Y)
            {
                public virtual bool Equals(Point? other) => other is not null && other.X == X;

                public override int GetHashCode() => X;
            }
            """
        );

        var dart = files["point.dart"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(dart).Contains("int get hashCode => this.x;");
        await Assert.That(dart).DoesNotContain("HashCode.combine");
        await Assert.That(dart).Contains("other is Point && this.equals(other)");
    }

    [Test]
    public async Task ConstructorWithOneDeferredInitializer_InTheDartTarget_PrintsABracedBody()
    {
        // Dart rejects an arrow body on a generative constructor
        // (`return_in_generative_constructor`).
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Money(int cents)
            {
                public int Twice { get; } = cents * 2;
            }
            """
        );

        var dart = files["money.dart"];

        await Assert.That(dart).DoesNotContain(") =>");
        await Assert.That(dart).Contains("{\n    this.twice = cents * 2;\n  }");
    }

    [Test]
    public async Task DartTarget_NullParamsArray_KeepsTheNullArgument()
    {
        // Dart lowers a params parameter to a plain list parameter, which can hold
        // null, so only the TypeScript rest-parameter lowering drops the argument.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Logger
            {
                public int Count(params string[]? items) => items?.Length ?? -1;

                public int Demo() => Count(null);
            }
            """
        );

        await Assert.That(files["logger.dart"]).Contains("count(null)");
    }

    [Test]
    public async Task EnumeratorType_MapsToDartIterator()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cursor
            {
                public IEnumerator<int>? Current { get; set; }
            }
            """
        );

        await Assert.That(files["cursor.dart"]).Contains("Iterator<int>?");
    }
}
