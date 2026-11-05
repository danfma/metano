using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The direct constructor call sites of spec 013 slice 013c (FR-017, FR-019,
/// FR-035, FR-011), pinned as goldens that compile under
/// <c>tsgo --strict --noImplicitOverride --declaration</c>. A <c>new</c>
/// Roslyn resolved into a dispatched set calls that overload's factory and a
/// <c>: base(...)</c> selects the base overload, both with Roslyn's bound
/// arguments; neither runs the dispatcher's runtime checks.
/// </summary>
public class OverloadCallSiteGoldenTests
{
    [Test]
    public async Task Construction_CallsTheResolvedOverloadsFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public string How { get; } = "";

                public Money(decimal amount) { How = "amount"; }

                public Money(int cents) { How = "cents"; }

                public Money(string code, int scale = 2) { How = code + scale; }
            }

            [Transpile]
            public class Box<T>
            {
                public T? Value { get; }

                public Box(T value) { Value = value; }

                public Box(int count) { }
            }

            [Transpile]
            public class Bag
            {
                public int Count { get; }

                public Bag(params int[] items) { Count = items.Length; }

                public Bag(string name) { Count = -1; }
            }

            [Transpile]
            public struct Pt
            {
                public int X;

                public Pt(int x) { X = x; }

                public Pt(int x, int y) { X = x + y; }
            }

            [Transpile]
            public class Single
            {
                public Single(int value) { }
            }

            [Transpile]
            public static class Shop
            {
                public static Money Price() => new Money(19.9m);

                public static Money Cents() => new(1990);

                public static Money Coded() => new Money("BRL");

                public static Money Named() => new Money(scale: 3, code: "USD");

                public static Box<string> Boxed() => new Box<string>("gift");

                public static Box<string> Counted() => new Box<string>(2);

                public static int Expanded() => new Bag(1, 2, 3).Count;

                public static int Empty() => new Bag().Count;

                public static int Spread(int[] items) => new Bag(items).Count;

                public static int Zero() => new Pt().X;

                public static int Pair() => new Pt(1, 2).X;

                public static Single One() => new Single(1);
            }
            """
        );

        await Assert
            .That(result["shop.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-new.ts"));
    }

    [Test]
    public async Task BaseCall_SelectsTheResolvedBaseOverloadWithExplicitDefaults()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base
            {
                public int A { get; }

                public int B { get; }

                public Base() { A = -1; }

                public Base(int a, int b = 5) { A = a; B = b; }

                public Base(string s) { A = s.Length; }
            }

            [Transpile]
            public class Derived : Base
            {
                public Derived(int c) : base(1) { }

                public Derived(string s) : base(s) { }

                public Derived() { }
            }

            [Transpile]
            public class Single : Base
            {
                public Single(int a) : base(a, 7) { }
            }

            [Transpile]
            public class Primary(int x) : Base(x);

            [Transpile]
            public class Chained : Base
            {
                public Chained() : this(3) { }

                public Chained(int c) : base(c, c) { }
            }

            [Transpile]
            public abstract class Shape
            {
                public string Kind { get; }

                protected Shape(int scale) { Kind = "scale"; }

                protected Shape(string name) { Kind = name; }
            }

            [Transpile]
            public class Circle : Shape
            {
                public Circle(int radius) : base(radius) { }
            }
            """
        );

        var outputs = string.Join(
            "\n",
            result["derived.ts"],
            result["single.ts"],
            result["primary.ts"],
            result["chained.ts"],
            result["circle.ts"]
        );

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-super.ts"));
    }

    [Test]
    public async Task JsonTypeSpec_ConstructsThroughTheJsonConstructorOverloadsFactory()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Text.Json.Serialization;

            [Transpile]
            public class Money
            {
                public decimal Amount { get; }

                public string Currency { get; } = "";

                public string? Note { get; init; }

                public Money(decimal amount) { Amount = amount; }

                [JsonConstructor]
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }

            [Transpile]
            public class Tag
            {
                public string Name { get; }

                public Tag(int id) { Name = id.ToString(); }

                [JsonConstructor]
                private Tag(string name) { Name = name; }
            }

            [Transpile]
            [JsonSerializable(typeof(Money))]
            [JsonSerializable(typeof(Tag))]
            public partial class ShopJsonContext : JsonSerializerContext
            {
                public ShopJsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }

                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var widened = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.ConstructorAccessibilityWidened)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        var outputs = string.Join("\n", files["shop-json-context.ts"], files["tag.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-json-factory.ts"));

        await Assert
            .That(widened)
            .IsEquivalentTo([
                "The factory of 'Tag(string name)' is emitted public instead of private because "
                    + "the TypeSpec of the JSON SerializerContext 'ShopJsonContext' calls it "
                    + "outside the TypeScript class body (spec 013 FR-011).",
            ]);

        await Assert
            .That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task NestedTypeConstruction_WidensTheFactoryItCalls()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Owner
            {
                public string How { get; } = "";

                public Owner(int id) { How = "id"; }

                private Owner(string code) { How = code; }

                private Owner(bool flag) { How = "flag"; }

                [Transpile]
                public class Maker
                {
                    public Owner Make() => new Owner("made");
                }
            }
            """
        );

        var widened = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.ConstructorAccessibilityWidened)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        await Assert
            .That(files["owner.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-nested.ts"));

        await Assert
            .That(widened)
            .IsEquivalentTo([
                "The factory of 'Owner(string code)' is emitted public instead of private because "
                    + "the nested type 'Owner.Maker' calls it outside the TypeScript class body "
                    + "(spec 013 FR-011).",
            ]);
    }

    [Test]
    public async Task ProjectReference_CallsTheProducersFactoriesAndSelectors()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            CrossPackageLibrary,
            CrossPackageConsumer
        );

        await Assert
            .That(result["app/till.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-cross-package.ts"));
    }

    /// <summary>
    /// DECIDED-24: a producer read from metadata only keeps the dispatcher
    /// calls — correct, only slower — and reports nothing.
    /// </summary>
    [Test]
    public async Task MetadataOnlyReference_KeepsTheDispatcherCalls()
    {
        var result = TranspileHelper.TranspileWithLibraryImage(
            CrossPackageLibrary,
            CrossPackageConsumer
        );

        await Assert
            .That(result["app/till.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-cross-package-metadata.ts"));
    }

    private const string CrossPackageLibrary = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("lib")]

        namespace Lib;

        public class Money
        {
            public string How { get; } = "";

            public Money(decimal amount) { How = "amount"; }

            public Money(int cents, int scale = 2) { How = "cents"; }
        }
        """;

    private const string CrossPackageConsumer = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        namespace App;

        public class Till : Lib.Money
        {
            public Till(int cents) : base(cents) { }

            public static Lib.Money Price() => new Lib.Money(5);
        }
        """;
}
