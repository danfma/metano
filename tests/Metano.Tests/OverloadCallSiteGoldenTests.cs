using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Transformation;

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
    /// <summary>
    /// One golden per construction shape: a decimal and an implicit
    /// <c>new(…)</c>, an omitted default and named arguments passed
    /// positionally, a generic class's type arguments, <c>params</c> expanded,
    /// empty and spread, a struct's implicit <c>new S()</c> kept, and a
    /// single-constructor class kept as <c>new</c>. (A single-constructor base
    /// keeping a plain <c>super(args)</c> is pinned by
    /// <c>ConstructorOverloadTests.DerivedWithOverloads_NonOverloadedBase_EachBranchCallsSuper</c>.)
    /// </summary>
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

        var widened = WidenedFactoryMessagesOf(diagnostics);

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

        var widened = WidenedFactoryMessagesOf(diagnostics);

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

    /// <summary>
    /// C# evaluates named arguments in source order, so arguments passed out
    /// of parameter order with a side effect are bound first, in that order:
    /// an IIFE for a construction, temporaries before <c>super(...)</c>.
    /// </summary>
    [Test]
    public async Task NamedArgumentsWithSideEffects_KeepTheSourceEvaluationOrder()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Calls
            {
                private static int _next;

                public static int F() => ++_next;
            }

            [Transpile]
            public class Base
            {
                public int A { get; }

                public int B { get; }

                public Base(int a, int b) { A = a; B = b; }

                public Base(string s) { A = s.Length; }
            }

            [Transpile]
            public class Derived : Base
            {
                public Derived() : base(b: Calls.F(), a: Calls.F()) { }
            }

            [Transpile]
            public static class Shop
            {
                public static Base Make() => new Base(b: Calls.F(), a: Calls.F());

                public static Base Plain(int x) => new Base(b: x, a: 1);
            }
            """
        );

        var outputs = string.Join("\n", result["derived.ts"], result["shop.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-evaluation-order.ts"));
    }

    /// <summary>
    /// The defaults an omitted parameter binds: a small integer as its
    /// number, an <c>[Optional]</c> reference parameter without a default as
    /// <c>null</c>.
    /// </summary>
    [Test]
    public async Task OmittedDefaults_PassTheirBoundValues()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Runtime.InteropServices;

            [Transpile]
            public class Opt
            {
                public Opt(int a, [Optional] object? o, byte b = 5, short c = -2) { }

                public Opt(string s) { }
            }

            [Transpile]
            public static class Use
            {
                public static Opt Make() => new Opt(1);
            }
            """
        );

        await Assert.That(result["use.ts"]).Contains("return Opt.fromAOBC(1, null, 5, -2);");
    }

    /// <summary>
    /// Dart constructs through the named constructor of the overload Roslyn
    /// selected, with the bound arguments — the omitted default passed
    /// explicitly (spec 013 FR-017, FR-022).
    /// </summary>
    [Test]
    public async Task Dart_ConstructsThroughTheNamedConstructor()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public class Base
            {
                public Base(int a, int b = 5) { }

                public Base(string s) { }
            }

            [Transpile]
            public static class Shop
            {
                public static Base Make() => new Base(1);
            }
            """
        );

        await Assert.That(files["shop.dart"]).Contains("static Base make() => Base.fromAB(1, 5);");
    }

    /// <summary>
    /// A caller's cached group is transformed again when the constructor set
    /// it calls changes the factory's name: its closure tracks signatures, not
    /// the body's <c>new</c>, so the key folds in every dispatched set.
    /// </summary>
    [Test]
    public async Task GroupCache_RetransformsACallerWhenTheFactoryRenames()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"metano-013c-{Guid.NewGuid():N}");

        Directory.CreateDirectory(cacheDir);

        try
        {
            var first = TransformWithCache(cacheDir, CachedShop("cents"));
            var second = TransformWithCache(cacheDir, CachedShop("units"));

            await Assert.That(first).Contains("Money.fromCents(1)");
            await Assert.That(second).Contains("Money.fromUnits(1)");
        }
        finally
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    private static List<string> WidenedFactoryMessagesOf(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.ConstructorAccessibilityWidened)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

    /// <summary>
    /// Transforms <paramref name="source"/> with the per-group cache in
    /// <paramref name="cacheDir"/>, writes the files there the way the host
    /// would, and returns <c>app/shop.ts</c> as written.
    /// </summary>
    private static string TransformWithCache(string cacheDir, string source)
    {
        var compilation = TranspileHelper.CompileLibrary(source);
        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);
        var printer = new Printer();

        var transformer = new TypeTransformer(ir, compilation)
        {
            CacheOutputDir = cacheDir,
            CacheConfigurationFingerprint = "fp",
        };

        foreach (var file in transformer.TransformAll())
        {
            var path = Path.Combine(cacheDir, file.FileName);

            var content = transformer.CachedFileContents.TryGetValue(file.FileName, out var cached)
                ? cached
                : printer.Print(file);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return File.ReadAllText(Path.Combine(cacheDir, "app", "shop.ts"));
    }

    private static string CachedShop(string centsName) =>
        $$"""
            namespace App;

            [Transpile]
            public class Money
            {
                public Money(decimal amount) { }

                public Money(int {{centsName}}) { }
            }

            [Transpile]
            public static class Shop
            {
                public static Money Make() => new Money(1);
            }
            """;

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
