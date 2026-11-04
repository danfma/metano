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

            public class Mixed<T>
            {
                public string Log { get; } = "";

                public Mixed(T x) { Log = "T"; }

                public Mixed(int x) { Log = "int"; }
            }
            """
        );

        await Assert
            .That(result["bag.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-generic-unconstrained.ts"));

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
}
