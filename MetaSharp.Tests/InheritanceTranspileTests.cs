namespace MetaSharp.Tests;

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
        await Assert.That(result["shape.ts"]).IsEqualTo(expected);
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
        await Assert.That(result["circle.ts"]).IsEqualTo(expected);
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

        var childTs = result["child.ts"];
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

        var childTs = result["child.ts"];
        // Only own params in constructor (base params are declared in parent)
        await Assert.That(childTs).Contains("constructor(readonly age: number)");
        await Assert.That(childTs).Contains("super(name)");
    }

    [Test]
    public async Task DerivedRecord_EqualsIncludesAllFields()
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

        var derivedTs = result["derived.ts"];
        // equals should check both x and y
        await Assert.That(derivedTs).Contains("this.x === other.x");
        await Assert.That(derivedTs).Contains("this.y === other.y");
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

        var extendedTs = result["models/extended.ts"];
        await Assert.That(extendedTs).Contains("from \"../base\"");
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
}
