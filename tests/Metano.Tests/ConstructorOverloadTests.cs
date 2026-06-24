namespace Metano.Tests;

public class ConstructorOverloadTests
{
    [Test]
    public async Task SingleConstructor_NoDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Simple
            {
                public int X { get; }
                public Simple(int x) { X = x; }
            }
            """
        );

        var output = result["simple.ts"];
        // Single constructor should NOT have overload signatures or ...args
        await Assert.That(output).DoesNotContain("...args");
        await Assert.That(output).Contains("constructor(");
    }

    [Test]
    public async Task TwoConstructors_GeneratesDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Point
            {
                public int X { get; }
                public int Y { get; }

                public Point(int x, int y) { X = x; Y = y; }
                public Point() { X = 0; Y = 0; }
            }
            """
        );

        var output = result["point.ts"];
        // Overload signatures are declaration-only — TS rejects parameter-
        // property modifiers (`public x`) on overload sigs, so the bridge
        // emits the plain `x: number` form on each signature and lets the
        // dispatcher impl produce the runtime members.
        await Assert.That(output).Contains("constructor(x: number, y: number);");
        await Assert.That(output).Contains("constructor();");
        // Should have dispatcher
        await Assert.That(output).Contains("...args: unknown[]");
        // Should have type checks
        await Assert.That(output).Contains("isInt32");
    }

    [Test]
    public async Task CopyConstructor_UsesInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Vec
            {
                public int X { get; }

                public Vec(int x) { X = x; }
                public Vec(Vec other) { X = other.X; }
            }
            """
        );

        var output = result["vec.ts"];
        await Assert.That(output).Contains("instanceof Vec");
    }

    [Test]
    public async Task DifferentPrimitiveTypes_UsesSpecializedChecks()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Converter
            {
                public string Value { get; }

                public Converter(int num) { Value = num.ToString(); }
                public Converter(string text) { Value = text; }
            }
            """
        );

        var output = result["converter.ts"];
        await Assert.That(output).Contains("isInt32");
        await Assert.That(output).Contains("isString");
    }

    [Test]
    public async Task ThreeConstructors_AllOverloadsGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Config
            {
                public string Name { get; }
                public int Value { get; }

                public Config(string name, int value) { Name = name; Value = value; }
                public Config(string name) { Name = name; Value = 0; }
                public Config() { Name = "default"; Value = 0; }
            }
            """
        );

        var output = result["config.ts"];
        // Three overload signatures (declaration-only — no parameter-property
        // modifiers, those would be a TS syntax error on an overload sig).
        await Assert.That(output).Contains("constructor(name: string, value: number);");
        await Assert.That(output).Contains("constructor(name: string);");
        await Assert.That(output).Contains("constructor();");
        await Assert.That(output).Contains("...args: unknown[]");
    }

    [Test]
    public async Task PrivateConstructor_BodyIsEmitted()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Counter
            {
                private readonly int _value;

                private Counter(int initial)
                {
                    _value = initial;
                }

                public int Value => _value;

                public static Counter Create() => new(42);
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("this._value = initial");
    }

    [Test]
    public async Task NonRecordClassWithSelfTypedCtor_BodyIsEmitted()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Box
            {
                private readonly int _value;

                public Box(int value)
                {
                    _value = value;
                }

                public Box(Box other)
                {
                    _value = other._value;
                }
            }
            """
        );

        var output = result["box.ts"];
        await Assert.That(output).Contains("this._value = value");
        await Assert.That(output).Contains("this._value = other._value");
    }

    [Test]
    public async Task DerivedWithOverloads_NonOverloadedBase_EachBranchCallsSuper()
    {
        // #25.2: derived class with two ctors over a base that has a single
        // ctor — each dispatcher branch must emit the matching super(...)
        // before inlining the body.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Base
            {
                public Base(int n) { N = n; }
                public int N { get; }
            }

            [Transpile]
            public class Derived : Base
            {
                public Derived(int n) : base(n) { Tag = "n"; }
                public Derived(string s) : base(s.Length) { Tag = s; }
                public string Tag { get; }
            }
            """
        );

        var output = result["app/derived.ts"];
        await Assert.That(output).Contains("extends Base");
        // Both branches reach a super(...) call before assigning the
        // derived-side property.
        await Assert.That(output).Contains("super(n)");
        await Assert.That(output).Contains("super(s.length)");
        await Assert.That(output).Contains("this.tag = \"n\"");
        await Assert.That(output).Contains("this.tag = s");
    }

    [Test]
    public async Task DerivedWithOverloads_OverloadedBase_SuperReachesBaseDispatcher()
    {
        // #25.2: derived's super(...) lands at the base's public constructor —
        // which is itself the dispatcher signature `(...args: unknown[])` when
        // the base has overloads. The runtime branch resolves at the base's
        // entry. This test pins the structural shape: both classes get their
        // own dispatcher and the derived branches call super with raw args.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Base
            {
                public Base(int n) { Kind = "n"; }
                public Base(string s) { Kind = s; }
                public string Kind { get; }
            }

            [Transpile]
            public class Derived : Base
            {
                public Derived(int n) : base(n) { Tag = n.ToString(); }
                public Derived(string s) : base(s) { Tag = s; }
                public string Tag { get; }
            }
            """
        );

        var baseTs = result["app/base.ts"];
        var derivedTs = result["app/derived.ts"];

        // Base carries the dispatcher.
        await Assert.That(baseTs).Contains("constructor(...args: unknown[])");
        await Assert.That(baseTs).Contains("isInt32(args[0])");
        await Assert.That(baseTs).Contains("isString(args[0])");

        // Derived carries its own dispatcher and forwards args to super.
        await Assert.That(derivedTs).Contains("extends Base");
        await Assert.That(derivedTs).Contains("constructor(...args: unknown[])");
        await Assert.That(derivedTs).Contains("super(n)");
        await Assert.That(derivedTs).Contains("super(s)");
    }

    [Test]
    public async Task DerivedWithOverloads_RecordBase_SuperHonorsPrimaryCtorShape()
    {
        // #25.2: derived record's super(...) targets a primary-ctor record
        // base. The record's primary ctor has parameter-property modifiers;
        // the derived branches must emit super(value) with the matching arg
        // count regardless.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public record BaseRec(int Value);

            [Transpile]
            public record Derived : BaseRec
            {
                public Derived(int value) : base(value) { Tag = "n"; }
                public Derived(string s) : base(s.Length) { Tag = s; }
                public string Tag { get; init; } = "";
            }
            """
        );

        var derivedTs = result["app/derived.ts"];
        await Assert.That(derivedTs).Contains("extends BaseRec");
        await Assert.That(derivedTs).Contains("super(value)");
        await Assert.That(derivedTs).Contains("super(s.length)");
    }
}
