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
    public async Task ExtensionOnDelegate_ImportsHelperLibraryAndCallsTheTopLevelFunction()
    {
        // The extension helper lives in its own file; both the reduced form
        // (`h.Get.Twice()`) and the static form (`DelExt.Twice(h.Get)`) must import
        // it and call the top-level function, not a `DelExt.` qualifier that the
        // [NoContainer] lowering never emits.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public delegate int Getter();

            [Transpile, NoContainer]
            public static class DelExt
            {
                public static int Twice(this Getter g) => g() * 2;
            }

            [Transpile]
            public class Holder
            {
                public Getter Get => () => 1;
            }

            [Transpile]
            public class Consumer
            {
                public int Reduced(Holder h) => h.Get.Twice();

                public int Static(Holder h) => DelExt.Twice(h.Get);
            }
            """
        );

        var consumer = files["consumer.dart"];
        await Assert.That(consumer).Contains("import 'del_ext.dart';");
        await Assert.That(consumer).Contains("int reduced(Holder h) => twice(h.get);");
        await Assert.That(consumer).Contains("int static(Holder h) => twice(h.get);");
        await Assert.That(consumer).DoesNotContain("DelExt.");
    }

    [Test]
    public async Task ExtensionOnNonDelegateReceiver_ImportsHelperLibrary()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Num { public int V => 3; }

            [Transpile, NoContainer]
            public static class NumExt
            {
                public static int Dbl(this Num n) => n.V * 2;
            }

            [Transpile]
            public class Consumer
            {
                public int Run(Num n) => n.Dbl();
            }
            """
        );

        var consumer = files["consumer.dart"];
        await Assert.That(consumer).Contains("import 'num_ext.dart';");
        await Assert.That(consumer).Contains("int run(Num n) => dbl(n);");
    }

    [Test]
    public async Task BodyOnlyTypeReference_ImportsTheDeclaringFile()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Order { public int Id { get; } public Order(int id) { Id = id; } }

            [Transpile]
            public class Factory
            {
                public int Make() => new Order(1).Id;
            }
            """
        );

        await Assert.That(files["factory.dart"]).Contains("import 'order.dart';");
    }

    [Test]
    public async Task ExtensionInPlainStaticClass_ImportsAndKeepsTheClassQualifier()
    {
        // A static class without [NoContainer] stays a Dart class of static methods,
        // so both call forms must keep the `NumExt.` qualifier and import its file.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Num { public int V => 3; }

            [Transpile]
            public static class NumExt
            {
                public static int Dbl(this Num n) => n.V * 2;
            }

            [Transpile]
            public class Consumer
            {
                public int Reduced(Num n) => n.Dbl();

                public int Static(Num n) => NumExt.Dbl(n);
            }
            """
        );

        var consumer = files["consumer.dart"];
        await Assert.That(consumer).Contains("import 'num_ext.dart';");
        await Assert.That(consumer).Contains("int reduced(Num n) => NumExt.dbl(n);");
        await Assert.That(consumer).Contains("int static(Num n) => NumExt.dbl(n);");
    }

    [Test]
    public async Task BodyReferenceToSameFileType_DoesNotSelfImport()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public static Counter Zero() => new Counter();

                public Counter Clone() => Counter.Zero();
            }
            """
        );

        await Assert.That(files["counter.dart"]).DoesNotContain("import 'counter.dart'");
    }

    [Test]
    public async Task BodyReferenceToBclStaticReceiver_AddsNoImport()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public int Largest(int a, int b) => Math.Max(a, b);
            }
            """
        );

        await Assert.That(files["calc.dart"]).Contains("int largest(int a, int b) =>");
        await Assert.That(files["calc.dart"]).DoesNotContain("import ");
    }

    [Test]
    public async Task BodyOnlyCastTypeTestLocalAndCatch_ImportTheDeclaringFiles()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Order { }

            [Transpile]
            public class Invoice { }

            [Transpile]
            public class Ticket { }

            [Transpile]
            public class TicketException : Exception { }

            [Transpile]
            public class Inspector
            {
                public bool IsOrder(object x) => x is Order;

                public object AsInvoice(object x) => (Invoice)x;

                public void Touch()
                {
                    Ticket? t = null;
                    try { }
                    catch (TicketException) { }
                }
            }
            """
        );

        var inspector = files["inspector.dart"];
        await Assert.That(inspector).Contains("import 'order.dart';");
        await Assert.That(inspector).Contains("import 'invoice.dart';");
        await Assert.That(inspector).Contains("import 'ticket.dart';");
        await Assert.That(inspector).Contains("import 'ticket_exception.dart';");
    }

    [Test]
    public async Task ExtensionCallsInFieldInitializers_ImportAndQualifyLikeBodies()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Num { public int V => 3; }

            [Transpile]
            public static class NumExt
            {
                public static int Dbl(this Num n) => n.V * 2;
            }

            [Transpile, NoContainer]
            public static class Flat
            {
                public static int Tri(this Num n) => n.V * 3;
            }

            [Transpile]
            public class Consumer
            {
                public int Field = new Num().Dbl();

                public int Field2 = new Num().Tri();
            }
            """
        );

        var consumer = files["consumer.dart"];
        await Assert.That(consumer).Contains("import 'flat.dart';");
        await Assert.That(consumer).Contains("import 'num_ext.dart';");
        await Assert.That(consumer).Contains("int field = NumExt.dbl(Num());");
        await Assert.That(consumer).Contains("int field2 = tri(Num());");
    }

    [Test]
    public async Task OverloadedMethods_EmitOneFastPathPerOverload()
    {
        // Dart has no overloading: each overload of a dispatched group is
        // declared under its fast path (spec 013 FR-022), with no dispatcher
        // and no "no overloading" diagnostic.
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

        await Assert.That(diagnostics).IsEmpty();

        await Assert
            .That(files["widget.dart"])
            .IsEqualTo(
                """
                class Widget {
                  void drawDefault() {
                  }

                  void drawTimes(int times) {
                  }
                }

                """
            );
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

    /// <summary>
    /// C# tuples are Dart records, whose <c>==</c> compares element-wise at
    /// every level (a <c>List</c> element by reference) — C#'s answers — so
    /// the nested value equality lowers to the native operator (#331).
    /// </summary>
    [Test]
    public async Task NestedTupleEquality_LowersToRecordEquality()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Nested(((int, int), int) a, ((int, int), int) b) =>
                    object.Equals(a, b);

                public static bool Guarded(((int, int), int)? a, ((int, int), int) b) =>
                    a?.Equals(b) == true;
            }
            """
        );

        var output = files["comparisons.dart"];

        await Assert.That(output).Contains("=> a == b;");

        await Assert
            .That(output)
            .Contains("=> (($receiver) => $receiver == null ? null : $receiver == b)(a) == true;");
    }

    /// <summary>
    /// A conditional <c>?.Equals</c> whose receiver is not a tuple keeps the
    /// general lowering — the receiver's own method runs, under the name its
    /// <c>[Name]</c> gives it (<c>isSame</c>), as its declaration does. The
    /// tuple recognizer must not hijack the override-chain root (#331).
    /// </summary>
    [Test]
    public async Task NonTupleConditionalEquals_KeepTheGeneralLowering()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public sealed class User
            {
                public string Name { get; }

                public User(string name) => Name = name;

                [Name("isSame")]
                public bool Equals(User? other) => Name == other?.Name;
            }

            [Transpile]
            public static class Comparisons
            {
                public static bool? Renamed(User? a, User b) => a?.Equals(b);

                public static bool? Stringy(string? a, string b) => a?.Equals(b);
            }
            """
        );

        var output = files["comparisons.dart"];

        await Assert
            .That(output)
            .Contains("static bool? renamed(User? a, User b) => a?.isSame(b);");

        await Assert
            .That(output)
            .Contains("static bool? stringy(String? a, String b) => a?.equals(b);");
    }

    /// <summary>
    /// A nullable-tuple receiver over a different instantiation lowers to
    /// C#'s answer — <c>false</c>, unless both operands can be <c>null</c>
    /// and are — never to an instance call that does not exist on a Dart
    /// record. An argument that may hold the tuple (an <c>object</c>)
    /// compares with the record <c>==</c> instead (#331).
    /// </summary>
    [Test]
    public async Task MismatchedNullableTupleReceiver_LowersToFalseUnlessBothNull()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool ValueArgument((int, int)? c, (int, long) d) => c.Equals(d);

                public static bool ObjectArgument((int, int)? e, object f) => e.Equals(f);
            }
            """
        );

        var output = files["comparisons.dart"];

        await Assert
            .That(output)
            .Contains("static bool valueArgument((int, int)? c, (int, int) d) => false;");

        await Assert
            .That(output)
            .Contains("static bool objectArgument((int, int)? e, Object f) => e == f;");
    }

    /// <summary>
    /// A non-nullable tuple receiver over a different instantiation folds
    /// to C#'s answer — <c>false</c> — instead of an instance call that
    /// does not exist on a Dart record (#331).
    /// </summary>
    [Test]
    public async Task MismatchedTupleReceiver_LowersToFalse()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool ValueMismatched((int, int) a, (int, long) b) => a.Equals(b);
            }
            """
        );

        await Assert
            .That(files["comparisons.dart"])
            .Contains("static bool valueMismatched((int, int) a, (int, int) b) => false;");
    }

    /// <summary>
    /// An argument the strongly-typed <c>Equals</c> converts to the
    /// receiver's tuple, one a struct-constrained type parameter carries,
    /// and an <c>object</c> on either side of the static form all compare
    /// with the record <c>==</c>, C#'s answer for each (#331).
    /// </summary>
    [Test]
    public async Task ConvertedAndGenericTupleArguments_LowerToRecordEquality()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool Converted((long, long) a, (int, int) b) => a.Equals(b);

                public static bool Constrained<T>((int, int) c, T d)
                    where T : struct => c.Equals(d);

                public static bool StaticBoxed(object e, (int, int) f) => object.Equals(e, f);
            }
            """
        );

        var output = files["comparisons.dart"];

        await Assert
            .That(output)
            .Contains("static bool converted((int, int) a, (int, int) b) => a == b;");

        await Assert
            .That(output)
            .Contains("static bool constrained<T>((int, int) c, T d) => c == d;");

        await Assert
            .That(output)
            .Contains("static bool staticBoxed(Object e, (int, int) f) => e == f;");
    }
}
