namespace Metano.Tests;

/// <summary>
/// Tests for auto-initialized property/field defaults. C# value types have a
/// deterministic <c>default(T)</c> at runtime, but the transpiler used to leave
/// uninitialized properties as `undefined` in TS — breaking equality checks and any
/// downstream code that assumed the C# default. This file pins the corrected
/// behavior for each value-type category.
/// </summary>
public class AutoInitDefaultTests
{
    [Test]
    public async Task EnumProperty_AutoInitsToFirstMember()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Status { Pending, Active, Closed }

            [Transpile]
            public class Order
            {
                public Status State { get; set; }
            }
            """
        );

        await Assert.That(result["order.ts"]).Contains("state: Status = Status.Pending");
    }

    [Test]
    public async Task EnumWithNonZeroFirstMember_PicksFirstMember()
    {
        // Even when the user assigns explicit numeric values, the "first member" is
        // still the one with the smallest value (typically the one declared first).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Priority { Low = 1, High = 2, Critical = 3 }

            [Transpile]
            public class Ticket
            {
                public Priority Level { get; set; }
            }
            """
        );

        await Assert.That(result["ticket.ts"]).Contains("level: Priority = Priority.Low");
    }

    [Test]
    public async Task IntProperty_AutoInitsToZero()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public int Count { get; set; }
                public long Total { get; set; }
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("count: number = 0");
        await Assert.That(output).Contains("total: number = 0");
    }

    [Test]
    public async Task BoolProperty_AutoInitsToFalse()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Flag
            {
                public bool IsActive { get; set; }
            }
            """
        );

        await Assert.That(result["flag.ts"]).Contains("isActive: boolean = false");
    }

    [Test]
    public async Task DecimalProperty_AutoInitsToNewDecimalZero()
    {
        // decimal goes through the BCL export map (decimal.js Decimal). The auto-init
        // matches the literal-handling form: new Decimal("0") rather than 0, so the
        // syntax is consistent with how decimal literals lower elsewhere.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public decimal Amount { get; set; }
            }
            """
        );

        await Assert.That(result["money.ts"]).Contains("amount: Decimal = new Decimal(\"0\")");
    }

    [Test]
    public async Task ExplicitInitializer_StillWins()
    {
        // When the user provides an explicit initializer, the auto-default doesn't
        // override it.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int Count { get; set; } = 42;
            }
            """
        );

        await Assert.That(result["sample.ts"]).Contains("count: number = 42");
        await Assert.That(result["sample.ts"]).DoesNotContain("count: number = 0");
    }

    [Test]
    public async Task NullableProperty_StillNull()
    {
        // Nullable properties continue to default to null (existing behavior, just
        // moved into the helper).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public string? Name { get; set; }
                public int? Count { get; set; }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("name: string | null = null");
        await Assert.That(output).Contains("count: number | null = null");
    }

    [Test]
    public async Task NonNullableStringProperty_NoAutoInit()
    {
        // Plain `string` (non-nullable) has no automatic default — C# would require
        // the user to provide one, and emitting `= ""` would be a guess. The field
        // stays uninitialized, mirroring the user's intent.
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public class Sample
            {
                public string Name { get; set; } = "";
            }
            """
        );

        // The user provided "", so it round-trips. The negative case (no init) is
        // covered by the absence of any auto-default.
        await Assert.That(result["sample.ts"]).Contains("name: string = \"\"");
    }

    [Test]
    public async Task PlainField_AlsoAutoInits()
    {
        // Same logic applies to plain fields, not just auto-properties.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int Count;
                public bool Open;
            }
            """
        );

        var output = result["box.ts"];
        await Assert.That(output).Contains("count: number = 0");
        await Assert.That(output).Contains("open: boolean = false");
    }

    [Test]
    public async Task StructTypedMembers_MatchTheGoldenOutput()
    {
        // C# zero-initializes a struct-typed field: a default-constructible
        // transpiled struct gets a `new S()` initializer. A struct that declares a
        // parameterized constructor has no parameterless TS construction, and one
        // that declares a parameterless constructor would run it where C#'s zero
        // value skips it (Tally.N is 0, not 5), so their fields keep no initializer
        // (#291 tracks a type-correct zero value).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Point
            {
                public int X;
                public int Y;
            }

            [Transpile]
            public struct Box<T>
            {
                public T Value;
            }

            [Transpile]
            public record struct Size
            {
                public int Width { get; init; }
            }

            [Transpile]
            public struct Celsius
            {
                public Celsius(double degrees) => Degrees = degrees;

                public double Degrees { get; }
            }

            [Transpile]
            public struct Counter
            {
                public int N;

                public Counter() { N = 5; }
            }

            [Transpile]
            public struct Segment
            {
                public Point Start;
                public Point? Origin;
                public Box<int> Boxed;
                public Size Extent { get; set; }
                public Celsius Temperature;
                public Counter Tally;
                public Point Fixed = new Point { X = 1 };

                public Segment() { }
            }
            """
        );

        await Assert
            .That(result["segment.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("struct-member-defaults.ts"));
    }

    [Test]
    public async Task StructTypedStaticAndClassMembers_AutoInitToNewInstance()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Point
            {
                public int X;
            }

            [Transpile]
            public class Shape
            {
                public Point Center { get; set; }
                public static Point Zero;
            }
            """
        );

        var output = result["shape.ts"];
        await Assert.That(output).Contains("center: Point = new Point();");
        await Assert.That(output).Contains("static zero: Point = new Point();");
        await Assert.That(output).Contains("import { Point } from \"./point\";");
    }

    [Test]
    public async Task PlainObjectAndPrimaryConstructorStructMembers_GetNoInstanceDefault()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record struct Wire(int Code);

            [Transpile]
            public readonly record struct Money(int Cents);

            [Transpile]
            public class Holder
            {
                public Wire Payload { get; set; }
                public Money Price { get; set; }
            }
            """
        );

        var output = result["holder.ts"];
        await Assert.That(output).DoesNotContain("new Wire");
        await Assert.That(output).DoesNotContain("new Money");
    }
}
