using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Overload dispatchers with type-parameter-typed parameters (#440): an
/// unconstrained or <c>struct</c> type parameter checks arity only, a
/// constrained one uses its constraint's check, and a concrete overload of
/// the same arity is tried before the generic one.
/// </summary>
public class OverloadDispatchGenericTests
{
    [Test]
    public async Task Constructor_UnconstrainedTypeParameter_ChecksArityOnly()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Bag<T>
            {
                public string Log { get; } = "";

                public Bag(T item) { Log = "item"; }

                public Bag(T item, int count) { Log = "count"; }
            }
            """
        );

        await Assert
            .That(result["bag.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-generic-unconstrained.ts"));
    }

    [Test]
    public async Task Constructor_ConcreteAndGenericOfSameArity_TriesConcreteFirst()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Mixed<T>
            {
                public string Log { get; } = "";

                public Mixed(T x) { Log = "T"; }

                public Mixed(int x) { Log = "int"; }
            }
            """
        );

        await Assert
            .That(result["mixed.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-generic-mixed.ts"));
    }

    [Test]
    public async Task Constructor_ConstrainedTypeParameter_UsesTheConstraintCheck()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public interface IShape { int Area(); }

            public class Animal { }

            public class RefBox<T> where T : class
            {
                public RefBox(T x) { }

                public RefBox(int x) { }
            }

            public class ShapeBox<T> where T : IShape
            {
                public ShapeBox(T x) { }

                public ShapeBox(int x) { }
            }

            public class AnimalBox<T> where T : Animal
            {
                public AnimalBox(T x) { }

                public AnimalBox(int x) { }
            }

            public class ValBox<T> where T : struct
            {
                public ValBox(T x) { }

                public ValBox(T x, int n) { }
            }
            """
        );

        var outputs = string.Join(
            "\n",
            result["ref-box.ts"],
            result["shape-box.ts"],
            result["animal-box.ts"],
            result["val-box.ts"]
        );

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-generic-constrained.ts"));
    }

    [Test]
    public async Task Methods_ClassAndMethodTypeParameters_ResolveTheirConstraints()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Holder<T>
            {
                public string Put(T x) => "T";

                public string Put(string x) => "string";

                public string Ref<U>(U u) where U : class => "U";

                public string Ref(int n) => "int";

                public string Opt(T? x = default) => "opt";

                public string Opt(int a, int b) => "ab";
            }
            """
        );

        await Assert
            .That(result["holder.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-generic-methods.ts"));
    }

    [Test]
    public async Task ClassConstraint_AdmitsNull()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Slot<T> where T : class?
            {
                public Slot(T value) { }

                public Slot(int index) { }
            }
            """
        );

        await Assert
            .That(result["slot.ts"])
            .Contains(
                "args.length === 1 && (typeof args[0] === \"object\" || typeof args[0] === \"function\" || typeof args[0] === \"string\")"
            );
    }

    [Test]
    public async Task Operators_TryTheConcreteOperandBeforeTheTypeParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Box<T>
            {
                public string Tag { get; }

                public Box(string tag) { Tag = tag; }

                public static Box<T> operator +(Box<T> a, T b) => new Box<T>("T");

                public static Box<T> operator +(Box<T> a, int b) => new Box<T>("int");
            }
            """
        );

        var output = result["box.ts"];
        var concrete = output.IndexOf(
            "args.length === 2 && args[0] instanceof Box && isInt32(args[1])"
        );
        var generic = output.IndexOf("args.length === 2 && args[0] instanceof Box)");

        await Assert.That(concrete).IsGreaterThanOrEqualTo(0);
        await Assert.That(generic).IsGreaterThan(concrete);
    }

    [Test]
    public async Task CatchAllOverload_OverlappingConcreteOptionalOrParams_ReportsMs0063()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Ctor<T>
            {
                public Ctor(T value) { }

                public Ctor(int a, int b = 9) { }

                public Ctor(string s, params string[] rest) { }
            }

            public class Calls<T>
            {
                public string F(T value) => "T";

                public string F(int a, int b = 9) => "ab";
            }
            """
        );

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .ToList();

        await Assert.That(warnings.Count).IsEqualTo(2);
        await Assert
            .That(warnings.All(w => w.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();

        var constructor = warnings.Single(w => w.Message.Contains("Ctor<T>.Ctor(T)"));

        await Assert.That(constructor.Message).Contains("'Ctor<T>.Ctor(int, int)'");
        await Assert.That(constructor.Message).Contains("'Ctor<T>.Ctor(string, params string[])'");
        await Assert.That(constructor.Message).Contains("#25");
        await Assert.That(constructor.Location).IsNotNull();

        var method = warnings.Single(w => w.Message.Contains("Calls<T>.F(T)"));

        await Assert.That(method.Message).Contains("'Calls<T>.F(int, int)'");
    }

    [Test]
    public async Task CatchAllOverload_TriedAfterTheConcreteOne_ReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Mixed<T>
            {
                public Mixed(T x) { }

                public Mixed(int x) { }
            }

            public class Bag<T>
            {
                public Bag(T item) { }

                public Bag(T item, int count) { }
            }

            public class Ranged<T> where T : class
            {
                public Ranged(T value) { }

                public Ranged(int a, int b = 9) { }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch))
            .IsEmpty();
    }
}
