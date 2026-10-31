namespace Metano.Tests;

public class InheritanceTranspileTests
{
    [Test]
    public async Task BaseRecord_GeneratesNormally()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Shape(int X, int Y);

                [Transpile]
                public record Circle(int X, int Y, double Radius) : Shape(X, Y);
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("inheritance-base.ts");
        await Assert.That(result["app/shape.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task DerivedRecord_ExtendsBaseWithSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Shape(int X, int Y);

                [Transpile]
                public record Circle(int X, int Y, double Radius) : Shape(X, Y)
                {
                    public double Area() => 3.14159 * Radius * Radius;
                }
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("inheritance-derived.ts");
        await Assert.That(result["app/circle.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task DerivedRecord_HasExtendsKeyword()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(string Name);

                [Transpile]
                public record Child(string Name, int Age) : Base(Name);
            }
            """
        );

        var childTs = result["app/child.ts"];
        await Assert.That(childTs).Contains("extends Base");
        await Assert.That(childTs).Contains("super(name)");
    }

    [Test]
    public async Task DerivedRecord_BaseParamsNotReadonly()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(string Name);

                [Transpile]
                public record Child(string Name, int Age) : Base(Name);
            }
            """
        );

        var childTs = result["app/child.ts"];
        // Forwarded base params stay in the signature (C# order) as plain
        // params; only own params become parameter properties.
        await Assert.That(childTs).Contains("constructor(name: string, readonly age: number)");
        await Assert.That(childTs).Contains("super(name)");
    }

    [Test]
    public async Task DerivedRecord_EqualsChainsToBaseForInheritedFields()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(int X);

                [Transpile]
                public record Derived(int X, int Y) : Base(X);
            }
            """
        );

        var derivedTs = result["app/derived.ts"];
        // equals should check both x and y
        // `x` belongs to the base: compared by `super.equals`, like C#'s
        // base.Equals, never re-read from the derived record.
        await Assert.That(derivedTs).Contains("return super.equals(other) && this.y === other.y;");
        await Assert.That(derivedTs).Contains("hc.add(super.hashCode());");
        await Assert.That(derivedTs).DoesNotContain("this.x === other.x");
    }

    [Test]
    public async Task DerivedRecord_ImportBaseType()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(int X);
            }

            namespace App.Models
            {
                [Transpile]
                public record Extended(int X, string Label) : App.Base(X);
            }
            """
        );

        var extendedTs = result["app/models/extended.ts"];
        await Assert.That(extendedTs).Contains("from \"#/app/base\"");
    }

    [Test]
    public async Task NonTranspiledBase_NoExtends()
    {
        var result = TranspileHelper.Transpile(
            """
            public record NotTranspiled(int X);

            [Transpile]
            public record Child(int X, int Y) : NotTranspiled(X);
            """
        );

        var childTs = result["child.ts"];
        // Should not extend a non-transpiled type
        await Assert.That(childTs).DoesNotContain("extends");
    }

    private const string ClipHierarchySource = """
        namespace Media
        {
            [Transpile]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [Transpile]
            public sealed record SilenceClip(double Seconds) : Clip(Seconds);
        }
        """;

    [Test]
    public async Task AbstractRecordBase_EmitsAbstractClassWithCopyingWith()
    {
        var result = TranspileHelper.Transpile(ClipHierarchySource);

        var expected = TranspileHelper.ReadExpected("derived-record-abstract-base.ts");
        await Assert.That(result["media/clip.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task DerivedRecord_BaseParamLast_KeepsCSharpParameterOrder()
    {
        // `seconds` is forwarded to the base and declared after the own
        // param: the signature and `super(...)` follow the C# primary
        // constructor order; equality compares the base member first, as
        // C# record equality does.
        var result = TranspileHelper.Transpile(ClipHierarchySource);

        var expected = TranspileHelper.ReadExpected("derived-record-forwarded-param.ts");
        await Assert.That(result["media/image-clip.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task DerivedRecord_WithOnlyForwardedParams_KeepsThemInSignature()
    {
        var result = TranspileHelper.Transpile(ClipHierarchySource);

        var silence = result["media/silence-clip.ts"];
        await Assert.That(silence).Contains("constructor(seconds: number) {");
        await Assert.That(silence).Contains("super(seconds);");
        await Assert.That(silence).Contains("return copyRecordWith<SilenceClip>(this, overrides);");
    }

    [Test]
    public async Task DerivedRecord_ThreeLevelChain_ComparesEveryForwardedField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record A(int X);

                [Transpile]
                public record B(int X, int Y) : A(X);

                [Transpile]
                public record C(int Z, int X, int Y) : B(X, Y);
            }
            """
        );

        var c = result["app/c.ts"];
        await Assert.That(c).Contains("constructor(readonly z: number, x: number, y: number) {");
        await Assert.That(c).Contains("super(x, y);");
        await Assert.That(c).Contains("super.equals(other) && this.z === other.z");
        await Assert.That(c).Contains("return copyRecordWith<C>(this, overrides);");
    }

    [Test]
    public async Task DerivedRecord_BaseInitializedWithConstant_StillComparesInheritedField()
    {
        // C# record equality compares the base record's members first, even
        // when the derived constructor does not forward them.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(string Name);

                [Transpile]
                public record Child(int Age) : Base("fixed");
            }
            """
        );

        var child = result["app/child.ts"];
        await Assert.That(child).Contains("constructor(readonly age: number) {");
        await Assert.That(child).Contains("super(\"fixed\");");
        await Assert.That(child).Contains("super.equals(other) && this.age === other.age");
    }

    [Test]
    public async Task DerivedRecord_WithoutBaseInitializer_CallsParameterlessSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public abstract record Base
                {
                    public int Version { get; init; }
                }

                [Transpile]
                public record Child(string Name) : Base;
            }
            """
        );

        var child = result["app/child.ts"];
        await Assert.That(child).Contains("constructor(readonly name: string) {");
        await Assert.That(child).Contains("super();");
    }

    [Test]
    public async Task DerivedRecord_ForwardedParamWithRenamedBaseProperty_ComparesRenamedMember()
    {
        // The forwarded parameter is plain; equality reads the inherited
        // property under its [Name], not under the parameter's name.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base([property: Name("label")] string Title);

                [Transpile]
                public record Child(string Title, int Rank) : Base(Title);
            }
            """
        );

        var child = result["app/child.ts"];
        await Assert.That(child).Contains("constructor(title: string, readonly rank: number) {");
        await Assert.That(child).Contains("super.equals(other) && this.rank === other.rank");
        await Assert.That(result["app/base.ts"]).Contains("this.label === other.label");
    }

    [Test]
    public async Task Record_InitPropertyOutsidePrimaryCtor_TakesPartInEquality()
    {
        // C# record equality compares every instance field, so an init-only
        // property the primary constructor does not take is compared too,
        // and `with` keeps it because it copies the instance.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Tagged(int Id)
                {
                    public string Tag { get; init; } = "";

                    public int Twice => Id * 2;
                }
            }
            """
        );

        var tagged = result["app/tagged.ts"];
        await Assert
            .That(tagged)
            .Contains(
                "other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.id === other.id && this.tag === other.tag"
            );
        await Assert.That(tagged).DoesNotContain("this.twice === other.twice");
        await Assert.That(tagged).Contains("return copyRecordWith<Tagged>(this, overrides);");
    }

    [Test]
    public async Task DerivedRecord_BaseWithPrivateField_ChainsInsteadOfReadingIt()
    {
        // The base's private field stays private: the derived helpers only
        // reach it through super.equals / super.hashCode (no TS2341).
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Account(string Owner)
                {
                    private readonly int _pin = 0;

                    public bool Check(int pin) => pin == _pin;
                }

                [Transpile]
                public record Savings(string Owner, decimal Rate) : Account(Owner);
            }
            """
        );

        var account = result["app/account.ts"];
        var savings = result["app/savings.ts"];
        await Assert.That(account).Contains("this._pin === other._pin");
        await Assert
            .That(savings)
            .Contains("return super.equals(other) && valueEquals(this.rate, other.rate);");
        await Assert.That(savings).Contains("hc.add(super.hashCode());");
        await Assert.That(savings).DoesNotContain("_pin");
    }
}
