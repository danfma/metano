using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The constructor factories and selector symbols of spec 013 slice 013b
/// (FR-007–FR-014, FR-026–FR-029, contracts/ts-emission.md), pinned as goldens
/// that compile under <c>tsgo --strict --noImplicitOverride --declaration</c>.
/// Every overload of a dispatched constructor gets a type-qualified
/// <c>protected static readonly</c> selector, first among the statics, a
/// selector signature, a guarded selector branch and — for a concrete class —
/// a factory with its overload's own visibility.
/// </summary>
public class OverloadFactoryGoldenTests
{
    [Test]
    public async Task DispatchedConstructor_EmitsSelectorsBranchesAndFactories()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public string How { get; } = "";

                public Money(decimal amount) { How = "amount"; }

                public Money(int cents) { How = "cents"; }
            }
            """
        );

        await Assert
            .That(result["money.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-basic.ts"));
    }

    [Test]
    public async Task HandWrittenConstruction_KeepsTheRuntimeSignaturesAndBranches()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Dimension
            {
                public int Size { get; }

                public int Width { get; }

                public int Height { get; }

                public Dimension(int size) { Size = size; }

                public Dimension(int width, int height) { Width = width; Height = height; }
            }
            """
        );

        await Assert
            .That(result["dimension.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-dispatcher-fallback.ts"));
    }

    [Test]
    public async Task Hierarchy_KeepsCompatibleFactoriesAndQualifiesTheOthers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public string How { get; } = "";

                public Money(decimal amount) { How = "amount"; }

                public Money(int cents) { How = "cents"; }

                private Money(string code) { How = "code"; }

                public Money(long id) { How = "id"; }
            }

            [Transpile]
            public class Receipt : Money
            {
                public Receipt(decimal amount) : base(amount) { }

                public Receipt(int cents) : base(cents) { }

                public Receipt(string code) : base(code.Length) { }

                protected Receipt(long id) : base(id) { }
            }

            [Transpile]
            public class GiftReceipt : Receipt
            {
                public string Note { get; } = "";

                public GiftReceipt(string amount) : base(amount.Length) { Note = amount; }

                public GiftReceipt(int cents) : base(cents) { }
            }
            """
        );

        var outputs = string.Join(
            "\n",
            result["money.ts"],
            result["receipt.ts"],
            result["gift-receipt.ts"]
        );

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-hierarchy.ts"));
    }

    /// <summary>
    /// The <c>MS0068</c> case (FR-034): the user static keeps its name, so the
    /// output fails <c>tsgo</c> with TS2417 — the build error says why.
    /// </summary>
    [Test]
    public async Task DerivedUserStatic_NamedLikeAnInheritedFactory_KeepsItsName()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                public Money(decimal amount) { }

                public Money(int cents) { }
            }

            [Transpile]
            public class Coupon : Money
            {
                public Coupon(int cents) : base(cents) { }

                public static Coupon FromAmount(string text) => new Coupon(text.Length);
            }
            """
        );

        await Assert
            .That(files["coupon.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-user-static.ts"));
    }

    [Test]
    public async Task GenericClass_FactoriesRedeclareTheTypeParametersWithTheirConstraints()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IRank<T>
            {
                int Rank(T other);
            }

            [Transpile]
            public class Box<T> where T : IRank<T>
            {
                public T? Value { get; }

                public int N { get; }

                public Box(T value) { Value = value; }

                public Box(int n) { N = n; }
            }

            [Transpile]
            public class RankedBox<T> : Box<T> where T : IRank<T>
            {
                public RankedBox(T value) : base(value) { }

                public RankedBox(int n) : base(n) { }
            }
            """
        );

        var outputs = string.Join("\n", result["box.ts"], result["ranked-box.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-generic.ts"));
    }

    [Test]
    public async Task ClosedGenericBase_QualifiesTheFactoryItCannotRedeclare()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box<T>
            {
                public T? Value { get; }

                public Box(T value) { Value = value; }

                public Box(int n) { }
            }

            [Transpile]
            public class IntBox : Box<int>
            {
                public IntBox(int value) : base(value) { }

                public IntBox(int value, int extra) : base(value + extra) { }
            }
            """
        );

        await Assert
            .That(result["int-box.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-generic-closed.ts"));
    }

    [Test]
    public async Task GenericDerived_ComparesConstraintsOnTheConstructedMapping()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IRank<T>
            {
                int Rank(T other);
            }

            [Transpile]
            public class Box<T>
            {
                public T? Value { get; }

                public Box(T value) { Value = value; }

                public Box(int n) { }
            }

            [Transpile]
            public class SortedBox<T> : Box<T> where T : IRank<T>
            {
                public SortedBox(T value) : base(value) { }

                public SortedBox(int n) : base(n) { }
            }

            [Transpile]
            public class Pair<K, V> : Box<K>
            {
                public Pair(K value) : base(value) { }

                public Pair(int n) : base(n) { }
            }
            """
        );

        var outputs = string.Join("\n", result["sorted-box.ts"], result["pair.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-generic-constraints.ts"));
    }

    [Test]
    public async Task AbstractClass_GetsSelectorsButNoFactories()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Shape
            {
                public int Scale { get; }

                protected Shape(int scale) { Scale = scale; }

                protected Shape(string name) { Scale = name.Length; }
            }

            [Transpile]
            public class Circle : Shape
            {
                public Circle(int radius) : base(radius) { }
            }
            """
        );

        var outputs = string.Join("\n", result["shape.ts"], result["circle.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-abstract.ts"));
    }

    [Test]
    public async Task PositionalRecordWithASecondaryConstructor_GetsAFactoryPerOverload()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Rec(int X)
            {
                public Rec() : this(1) { }
            }
            """
        );

        await Assert
            .That(result["rec.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-record.ts"));
    }

    [Test]
    public async Task ThisChain_StaysInlinedInTheSelectorBranch()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Temp
            {
                public int A { get; }

                public Temp(int a) { A = a; }

                public Temp() : this(42) { }
            }
            """
        );

        await Assert
            .That(result["temp.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-chain.ts"));
    }

    [Test]
    public async Task EachFactory_KeepsItsOverloadsVisibility()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Acct
            {
                public string How { get; } = "";

                public Acct(int id) { How = "id"; }

                private Acct(string code) { How = "code"; }

                protected Acct(int id, string code) { How = "idcode"; }

                internal Acct(bool flag) { How = "flag"; }

                protected internal Acct(double rate) { How = "rate"; }

                private protected Acct(char tag) { How = "tag"; }

                public static Acct Parse(string code) => new Acct(code);
            }
            """
        );

        await Assert
            .That(result["acct.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-accessibility.ts"));
    }

    [Test]
    public async Task Struct_GetsItsImplicitParameterlessConstructorAsABranch()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Pt
            {
                public int X;

                public string Name;

                public Pt(int x) { X = x; Name = "a"; }

                public Pt(int x, int y) { X = x + y; Name = "b"; }
            }

            [Transpile]
            public struct Tick
            {
                public int N { get; }

                public Tick() { N = 7; }

                public Tick(int n) { N = n; }
            }

            [Transpile]
            public class Use
            {
                public int Origin() => new Pt().X;
            }
            """
        );

        var outputs = string.Join("\n", result["pt.ts"], result["tick.ts"], result["use.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-struct.ts"));
    }

    [Test]
    public async Task ParamsAndDefaults_AreRestAndDefaultParametersOfTheFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Opt
            {
                public string Log { get; } = "";

                public Opt(int a = 5) { Log = "int:" + a; }

                public Opt(string s, params int[] rest) { Log = "params:" + s + ":" + rest.Length; }
            }
            """
        );

        await Assert
            .That(result["opt.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-params.ts"));
    }

    [Test]
    public async Task StaticInitializer_FindsTheSelectorsInitialized()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public static readonly Money Zero = new Money(0);

                public string How { get; } = "";

                public Money(decimal amount) { How = "amount"; }

                public Money(int cents) { How = "cents"; }
            }
            """
        );

        await Assert
            .That(result["money.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-static-init.ts"));
    }

    [Test]
    public async Task StaticInitializersAcrossAModuleCycle_ReadNoSelectorWhileModulesEvaluate()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class A
            {
                public static readonly B Seed = new B(1);

                public int Read(B b) => b.V;
            }

            [Transpile]
            public class B
            {
                public int V { get; }

                public B(int v) { V = v; }

                public B(string text) { V = text.Length; }

                public int Twice() => new A().Read(this) * 2;
            }
            """
        );

        var outputs = string.Join("\n", result["a.ts"], result["b.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-static-init-cycle.ts"));
    }

    [Test]
    public async Task ClaimedNames_TakeADollarPerSuffix()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Cash
            {
                public string How { get; } = "";

                public Cash(decimal amount) { How = "amount"; }

                public Cash(int cents) { How = "cents"; }

                public static Cash From(decimal amount) => new Cash(amount);

                public static Cash From(int cents) => new Cash(cents);
            }

            [Transpile]
            public class Fee
            {
                public static int FromCents = 3;

                public Fee(int cents) { }

                public Fee(string label) { }
            }
            """
        );

        var outputs = string.Join("\n", result["cash.ts"], result["fee.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-names-claims.ts"));
    }

    [Test]
    public async Task GenericHierarchy_RebindsInheritedFactoriesThroughEachExtendsClause()
    {
        // B reorders A's type parameters and declares one constructor, so C
        // inherits A's factory through B: C's `(P k, Q v)` is A's `(V v, K k)`
        // read through B, a different list, and C qualifies its factory.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class A<K, V>
            {
                public K? Key { get; }

                public A(K k, V v) { Key = k; }

                public A(int n) { }
            }

            [Transpile]
            public class B<V2, K2> : A<K2, V2>
            {
                public B(K2 k, V2 v) : base(k, v) { }
            }

            [Transpile]
            public class C<P, Q> : B<P, Q>
            {
                public C(P k, Q v) : base(v, k) { }

                public C(Q v, P k, bool flip) : base(v, k) { }
            }
            """
        );

        await Assert
            .That(result["c.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-hierarchy-generic.ts"));
    }

    [Test]
    public async Task StructDefaultBranch_GivesEachFieldItsTypesDefault()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public enum Mood { Calm, Loud }

            [Transpile]
            public struct Inner
            {
                public int V;

                public Inner(int v) { V = v; }

                public Inner(string text) { V = text.Length; }
            }

            [Transpile]
            public struct Day
            {
                public char Tag;

                public string Label;

                public Mood Mood;

                public Inner Nested;

                public Guid Id;

                public Day(int n) { Tag = 'a'; Label = ""; Mood = Mood.Loud; Nested = new Inner(n); Id = Guid.Empty; }

                public Day(string label) : this(label.Length) { Label = label; }
            }
            """
        );

        await Assert
            .That(files["day.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-struct-defaults.ts"));

        var unrepresentable = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .Where(message => message.Contains("FR-027"))
            .ToList();

        await Assert.That(unrepresentable.Count).IsEqualTo(1);
        await Assert
            .That(unrepresentable[0])
            .IsEqualTo(
                "The default value of 'System.Guid' has no literal in the target: 'new Day()' "
                    + "leaves 'id' undefined instead of the zeroed value (spec 013 FR-027)."
            );
    }

    [Test]
    public async Task CrossPackageBase_KeepsTheFactoriesItCanRedeclare()
    {
        var library = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class Money
            {
                public Money(decimal amount) { }

                public Money(int cents) { }
            }
            """;

        var consumer = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            public class Receipt : Lib.Money
            {
                public Receipt(decimal amount) : base(amount) { }

                public Receipt(string cents) : base(cents.Length) { }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);

        await Assert
            .That(result["app/receipt.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-factory-cross-package.ts"));
    }
}
