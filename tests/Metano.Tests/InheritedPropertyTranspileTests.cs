using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Properties declared against an inherited property: abstract declarations,
/// the positional record property C# synthesizes to override an abstract one,
/// and a property that hides an inherited one.
/// </summary>
public class InheritedPropertyTranspileTests
{
    private const string PositionalOverrideSource = """
        [Transpile]
        public abstract record Entry
        {
            public abstract int Id { get; init; }
        }

        [Transpile]
        public record Note(int Id, string Text) : Entry
        {
            public int NextId() => Id + 1;
        }
        """;

    [Test]
    public async Task PositionalRecordProperty_OverridingAbstractProperty_IsAssignedByTheConstructor()
    {
        // C# synthesizes `override int Id { get; init; }` for the positional
        // parameter and assigns it the argument; the base declares no storage.
        var result = TranspileHelper.Transpile(PositionalOverrideSource);

        var expected = TranspileHelper.ReadExpected("positional-override-abstract-property.ts");
        await Assert.That(result["note.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task AbstractRecordProperty_EmitsAbstractDeclarationWithoutStorage()
    {
        var result = TranspileHelper.Transpile(PositionalOverrideSource);

        var entry = result["entry.ts"];
        await Assert.That(entry).Contains("abstract readonly id: number;");
        await Assert.That(entry).DoesNotContain("readonly id: number = 0");
    }

    [Test]
    public async Task PositionalOverride_InIntermediateAbstractRecord_IsAssigned()
    {
        // L2 synthesizes the override; L3 inherits it (not abstract any more)
        // and only forwards its argument to the base constructor.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record L1 { public abstract int X { get; init; } }

            [Transpile]
            public abstract record L2(int X) : L1;

            [Transpile]
            public record L3(int X, int Y) : L2(X + 100);
            """
        );

        await Assert.That(result["l2.ts"]).Contains("constructor(readonly x: number) {");
        await Assert.That(result["l3.ts"]).Contains("constructor(x: number, readonly y: number) {");
        await Assert.That(result["l3.ts"]).Contains("super(x + 100);");
    }

    [Test]
    public async Task PositionalOverride_OfGenericAbstractProperty_IsAssigned()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record Box<T> { public abstract T Value { get; init; } }

            [Transpile]
            public record IntBox(int Value) : Box<int>;
            """
        );

        await Assert.That(result["box.ts"]).Contains("abstract readonly value: T;");
        await Assert.That(result["int-box.ts"]).Contains("constructor(readonly value: number) {");
    }

    [Test]
    public async Task PositionalOverride_WhoseParameterAnInitializerWrites_KeepsTheArgument()
    {
        // C# assigns the positional property before any initializer runs, so
        // `X++` changes only the parameter the later initializer reads.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record Entry { public abstract int X { get; init; } }

            [Transpile]
            public record Counter(int X) : Entry
            {
                public int Y { get; } = X++;
                public int Z { get; } = X;
            }
            """
        );

        var counter = result["counter.ts"];
        await Assert.That(counter).Contains("constructor(readonly x: number) {");
        await Assert.That(counter).Contains("this.y = x++;");
        await Assert.That(counter).Contains("this.z = x;");
    }

    [Test]
    public async Task ExplicitOverride_InitializedFromParameter_KeepsItsInitializer()
    {
        // A declared override is not an alias of the parameter, so it keeps
        // its own declaration and initializer.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record Entry { public abstract int X { get; init; } }

            [Transpile]
            public record Tripled(int X) : Entry
            {
                public override int X { get; init; } = X * 3;
            }
            """
        );

        var tripled = result["tripled.ts"];
        await Assert.That(tripled).Contains("constructor(x: number) {");
        await Assert.That(tripled).Contains("this.x = x * 3;");
    }

    [Test]
    public async Task AbstractGetOnlyProperty_EmitsAbstractDeclarationImplementedByGetterAndField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Shape
            {
                public abstract string Kind { get; }
                public string Describe() => "shape:" + Kind;
            }

            [Transpile]
            public class Circle : Shape
            {
                public override string Kind => "circle";
            }

            [Transpile]
            public class Square : Shape
            {
                public override string Kind { get; } = "square";
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("abstract-property-base.ts");
        await Assert.That(result["shape.ts"]).IsEqualTo(expected);
        await Assert.That(result["circle.ts"]).Contains("get kind(): string {");
        await Assert.That(result["square.ts"]).Contains("readonly kind: string = \"square\";");
    }

    [Test]
    public async Task AbstractReadWriteAndProtectedProperties_KeepMutabilityAndAccessibility()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Named
            {
                public abstract string Name { get; set; }
                protected abstract string? Note { get; }
            }
            """
        );

        var named = result["named.ts"];
        await Assert.That(named).Contains("  abstract name: string;");
        await Assert.That(named).Contains("protected abstract readonly note: string | null;");
    }

    [Test]
    public async Task PropertyHidingInheritedProperty_ReportsSharedStorage()
    {
        // C# keeps B.X and D.X apart (((B)d).X == 2); the generated class has
        // a single `x`, so the hiding is reported instead of silently merged.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record B(int X);

            [Transpile]
            public record D(int X) : B(X)
            {
                public new int X { get; init; } = X * 10;
            }
            """
        );

        var hiding = diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
        await Assert.That(hiding.Count).IsEqualTo(1);
        await Assert.That(hiding[0].Message).Contains("'D.X' hides the inherited 'B.X'");
    }

    [Test]
    public async Task OverridesAndUnrelatedMembers_ReportNoHiding()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Base
            {
                public abstract int X { get; }
                public virtual int Y { get; } = 1;
                private int _z;
                public static int S { get; } = 0;
            }

            [Transpile]
            public class Derived : Base
            {
                public override int X => 2;
                public override int Y { get; } = 3;
                private int _z;
                public static int S { get; } = 4;
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsFalse();
    }
}
