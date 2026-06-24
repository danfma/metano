namespace Metano.Tests;

/// <summary>
/// Plain (non-record) class inheritance — emits TypeScript
/// <c>class A extends B</c> with the appropriate <c>super(...)</c> call,
/// honors abstract modifiers, and routes captured / promoted /
/// inherited primary-ctor params through the existing constructor
/// pipeline.
/// </summary>
public class ClassInheritanceTests
{
    [Test]
    public async Task PlainClass_ExtendsBaseAndCallsSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Animal
            {
                public string Sound() => "...";
            }

            [Transpile]
            public class Dog : Animal
            {
                public string Bark() => "woof";
            }
            """
        );

        var output = result["app/dog.ts"];
        await Assert.That(output).Contains("extends Animal");
        // Synthesized derived ctor (no explicit ctor in C#) must still
        // forward to super so the emitted TS satisfies the type
        // checker's "derived class must call super before reading
        // this" rule.
        await Assert.That(output).Contains("super(");
    }

    [Test]
    public async Task AbstractBase_KeepsAbstractModifier_DerivedDoesNot()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public abstract class Shape
            {
                public abstract double Area();
            }

            [Transpile]
            public class Circle(double radius) : Shape
            {
                public override double Area() => 3.14159 * radius * radius;
            }
            """
        );

        var shape = result["app/shape.ts"];
        var circle = result["app/circle.ts"];

        await Assert.That(shape).Contains("export abstract class Shape");
        await Assert.That(shape).Contains("abstract area(): number");
        await Assert.That(circle).Contains("export class Circle extends Shape");
        await Assert.That(circle).DoesNotContain("abstract");
    }

    [Test]
    public async Task PrimaryCtor_WithBaseArgs_ForwardsViaSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Animal(string name)
            {
                public string Name => name;
            }

            [Transpile]
            public class Dog(string name, string breed) : Animal(name)
            {
                public string Breed => breed;
            }
            """
        );

        var output = result["app/dog.ts"];
        await Assert.That(output).Contains("extends Animal");
        await Assert.That(output).Contains("super(name)");
    }

    [Test]
    public async Task ExplicitCtor_WithBaseInitializer_ForwardsViaSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Animal
            {
                public string Name { get; }
                public Animal(string name) { Name = name; }
            }

            [Transpile]
            public class Dog : Animal
            {
                public string Breed { get; }
                public Dog(string name, string breed) : base(name) { Breed = breed; }
            }
            """
        );

        var output = result["app/dog.ts"];
        await Assert.That(output).Contains("extends Animal");
        await Assert.That(output).Contains("super(name)");
    }

    [Test]
    public async Task NonTranspilableBase_SkipsExtendsClause()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            // No [Transpile] — base is not part of the emitted surface
            public class External
            {
                public string Tag => "x";
            }

            [Transpile]
            public class Wrapper : External
            {
                public string Read() => "stuff";
            }
            """
        );

        var output = result["app/wrapper.ts"];
        await Assert.That(output).DoesNotContain("extends");
    }

    [Test]
    public async Task AbstractRecord_SuppressesAbstractModifier()
    {
        // Records get synthesized `with(...)` that calls `new
        // TypeName(...)`, which TypeScript rejects on abstract
        // classes. Skip the abstract modifier on records until
        // record synthesis grows a constructor-aware shape.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public abstract record Animal(string Name);

            [Transpile]
            public sealed record Dog(string Name, string Breed) : Animal(Name);
            """
        );

        var animal = result["app/animal.ts"];
        await Assert.That(animal).Contains("export class Animal");
        await Assert.That(animal).DoesNotContain("abstract class Animal");
    }

    [Test]
    public async Task ObjectBase_DoesNotEmitExtends()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Plain
            {
                public string Hello() => "hi";
            }
            """
        );

        var output = result["app/plain.ts"];
        // System.Object is the implicit base — no extends clause should
        // surface even though Roslyn reports it as the BaseType symbol.
        await Assert.That(output).DoesNotContain("extends");
    }
}
