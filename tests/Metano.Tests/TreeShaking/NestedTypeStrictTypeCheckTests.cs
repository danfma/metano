namespace Metano.Tests.TreeShaking;

/// <summary>
/// The hoisted nested types of spec 011 FR-040 type-check under <c>tsgo --strict</c>:
/// a library declares them module-level (<c>Owner$Inner</c>), exposes them through
/// static getters and an erased <c>declare namespace</c>, and a file of the library
/// and a consumer package reach them as <c>Owner.Inner</c> — as values (construction,
/// statics, enum members, <c>instanceof</c>) and as types (generic, nested-of-nested,
/// interface, delegate), and a derived owner hides an inherited nested type with
/// <c>new</c>, whose getter overrides the base one (<c>noImplicitOverride</c>).
/// </summary>
public class NestedTypeStrictTypeCheckTests
{
    private const string Library = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("acme-lib")]

        namespace Acme;

        public class Owner
        {
            public static readonly Owner Seed = new(new Inner(1));

            public Owner(Inner inner) { First = inner; }

            public Inner First { get; }

            public record Inner(int Value)
            {
                public static readonly Inner Zero = new(0);

                public static Inner Of(int value) => new(value);
            }

            public enum Kind { A, B }

            public sealed class Box<T> where T : class
            {
                public Box(T value) { Value = value; }

                public T Value { get; }
            }

            public sealed class Mid
            {
                public sealed record Leaf(Kind Kind);

                public static Leaf Make() => new(Kind.B);
            }

            public interface IShape
            {
                int Area();
            }

            public delegate int Measure(IShape shape);

            public sealed class Special : Owner
            {
                public Special() : base(Inner.Zero) { }
            }
        }

        public class Derived : Owner
        {
            public Derived() : base(Inner.Zero) { }

            public new sealed record Inner(int Value, int Extra) : Owner.Inner(Value);

            public new enum Kind { C }
        }

        public static class Reader
        {
            public static int Read(Owner.Inner inner) => inner.Value + Owner.Inner.Zero.Value;
        }
        """;

    private const string Consumer = """
        [assembly: TranspileAssembly]

        namespace App;

        using Acme;

        public sealed class Square : Owner.IShape
        {
            public int Area() => 4;
        }

        public static class Use
        {
            public static int Total()
            {
                var inner = Owner.Inner.Of(2);
                var box = new Owner.Box<string>("x");
                var leaf = Owner.Mid.Make();
                Owner.Measure measure = shape => shape.Area();
                object special = new Owner.Special();

                return inner.Value
                    + box.Value.Length
                    + (leaf.Kind == Owner.Kind.B ? 1 : 0)
                    + measure(new Square())
                    + (special is Owner ? 1 : 0)
                    + Reader.Read(Owner.Seed.First)
                    + new Derived.Inner(1, 2).Extra
                    + (int)Derived.Kind.C;
            }

            public static Owner.Mid.Leaf Leaf() => new(Owner.Kind.A);
        }
        """;

    [Test]
    public async Task CrossPackageNestedTypes_TypeCheckUnderStrict()
    {
        var library = TranspileHelper.Transpile(Library);
        var consumer = TranspileHelper.TranspileWithLibrary(Library, Consumer);

        var owner = library["acme/owner.ts"];

        await Assert.That(owner).Contains("export declare namespace Owner {");
        await Assert.That(owner).DoesNotContain("\nexport namespace");
        await Assert.That(consumer["app/use.ts"]).Contains("from \"acme-lib/acme\"");

        var (exitCode, output) = await StrictTypeCheck.Run(library, consumer);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
