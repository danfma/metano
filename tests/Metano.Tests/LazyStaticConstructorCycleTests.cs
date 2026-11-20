namespace Metano.Tests;

/// <summary>Initializer cycle ordering and beforefieldinit storage deferral (FR-066, #696).</summary>
public class LazyStaticConstructorCycleTests
{
    [Test]
    public async Task ThreeLevelCycle_ListsTheLeafBeforeTheMiddleAndUnrelatedConsumer()
    {
        var files = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Atlas
            {
                public static string Read() => Shape.Default.Name;
            }

            public class Shape
            {
                public static Shape Default;

                static Shape()
                {
                    Default = new Circle();
                    Default = new Tiny();
                }

                public virtual string Name => "shape";
            }

            public class Circle : Shape
            {
                public override string Name => "circle";
            }

            public class Tiny : Circle
            {
                public override string Name => "tiny";
            }
            """
        );

        var barrel = files["index.ts"];
        var leaf = barrel.IndexOf("./tiny", StringComparison.Ordinal);
        var middle = barrel.IndexOf("./circle", StringComparison.Ordinal);

        await Assert.That(leaf).IsGreaterThanOrEqualTo(0);
        await Assert.That(middle).IsGreaterThan(leaf);

        await Assert
            .That(barrel.IndexOf("./atlas", StringComparison.Ordinal))
            .IsGreaterThan(middle);

        await Assert.That(files["circle.ts"]).DoesNotContain("#$init");
        await Assert.That(files["tiny.ts"]).DoesNotContain("#$init");
    }

    [Test]
    [Arguments("public static readonly Dog Rex = new Dog();")]
    [Arguments("public static Dog Rex { get; } = new();")]
    public async Task LazyAncestorReadingDerivedSingleton_DefersOnlyReferencedInitializedDescendant(
        string singleton
    )
    {
        var files = TranspileHelper.Transpile(
            $$"""
            [assembly: TranspileAssembly]

            public class Animal
            {
                public static Animal Favorite;

                static Animal() { Favorite = Dog.Rex; }
            }

            public class Dog : Animal
            {
                {{singleton}}
            }

            public class Cat : Animal
            {
                public static readonly Cat Instance = new Cat();
            }

            public class Unrelated
            {
                public static readonly Unrelated Instance = new Unrelated();
            }
            """
        );

        var dog = files["dog.ts"];

        await Assert.That(dog).Contains("typeInitializer(\"Dog\", () => {");
        await Assert.That(dog).Contains("Dog.#rex$ = new Dog();");
        await Assert.That(dog).Contains("static get rex(): Dog {");
        await Assert.That(dog).DoesNotContain("static readonly rex: Dog = new Dog();");
        await Assert.That(files["cat.ts"]).DoesNotContain("#$init");
        await Assert.That(files["unrelated.ts"]).DoesNotContain("#$init");
    }

    [Test]
    public async Task TransitiveAncestorReadingSingleton_DefersTheLeafAndHoistsItFirst()
    {
        var files = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Animal
            {
                public static Animal Favorite;

                static Animal() { Favorite = Dog.Rex; }
            }

            public class Canine : Animal { }

            public class Dog : Canine
            {
                public static readonly Dog Rex = new();
            }
            """
        );

        var barrel = files["index.ts"];

        await Assert.That(files["dog.ts"]).Contains("Dog.#rex$ = new Dog();");
        await Assert.That(files["canine.ts"]).DoesNotContain("#$init");

        await Assert
            .That(barrel.IndexOf("./dog", StringComparison.Ordinal))
            .IsLessThan(barrel.IndexOf("./canine", StringComparison.Ordinal));
    }

    [Test]
    public async Task ColocatedSingletonCycle_StillDefersTheDerivedStorage()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("animals")]
            public class Animal
            {
                public static Animal Favorite;

                static Animal() { Favorite = Dog.Rex; }
            }

            [Transpile, EmitInFile("animals")]
            public class Dog : Animal
            {
                public static readonly Dog Rex = new();
            }
            """
        )["animals.ts"];

        await Assert.That(output).Contains("Dog.#rex$ = new Dog();");

        await Assert
            .That(output.IndexOf("export class Animal", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("export class Dog", StringComparison.Ordinal));
    }
}
