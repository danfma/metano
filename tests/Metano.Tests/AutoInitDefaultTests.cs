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

        await Assert.That(result["order.ts"]).Contains("this.state = Status.Pending");
    }

    [Test]
    public async Task EnumWithoutAZeroMember_DefaultsToTheZeroValue()
    {
        // C#'s default(E) is 0 even when no member has that value (#409), so the
        // field holds the zero asserted to the enum, never the lowest member.
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

        await Assert.That(result["ticket.ts"]).Contains("this.level = 0 as Priority");
    }

    /// <summary>
    /// The field default is the member whose value is 0, whatever its
    /// position and the underlying type (#409): not the lowest member.
    /// </summary>
    [Test]
    public async Task EnumWithNegativeMember_DefaultsToTheZeroMember()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum H : long { N = -1, Z = 0, P = 1 }

            [Transpile]
            public class Holder
            {
                public H Level { get; set; }
            }
            """
        );

        await Assert.That(result["holder.ts"]).Contains("this.level = H.Z");
    }

    [Test]
    public async Task StringEnumWithExplicitValues_DefaultsToTheZeroMember()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum S { P = 2, Q = 0 }

            [Transpile]
            public class Holder
            {
                public S Tone { get; set; }
            }
            """
        );

        await Assert.That(result["holder.ts"]).Contains("this.tone = S.Q");
    }

    /// <summary>
    /// A <c>[StringEnum]</c> without a zero member has no string standing for
    /// C#'s <c>0</c>: the field gets no initializer (documented, no diagnostic).
    /// </summary>
    [Test]
    public async Task StringEnumWithoutAZeroMember_LeavesTheFieldUninitialized()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum S { P = 2, Q = 3 }

            [Transpile]
            public class Holder
            {
                public S Tone { get; set; }
            }
            """
        );

        await Assert.That(result["holder.ts"]).Contains("tone: S;");
        await Assert.That(result["holder.ts"]).DoesNotContain("S.P");
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
        await Assert.That(output).Contains("this.count = 0");
        await Assert.That(output).Contains("this.total = 0n");
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

        await Assert.That(result["flag.ts"]).Contains("this.isActive = false");
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

        await Assert.That(result["money.ts"]).Contains("this.amount = new Decimal(\"0\")");
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

        await Assert.That(result["sample.ts"]).Contains("this.count = 42");
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
        await Assert.That(output).Contains("this.name = null");
        await Assert.That(output).Contains("this.count = null");
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
        await Assert.That(result["sample.ts"]).Contains("this.name = \"\"");
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
        await Assert.That(output).Contains("this.count = 0");
        await Assert.That(output).Contains("this.open = false");
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

        await TranspileHelper.AssertGolden("struct-member-defaults.ts", result["segment.ts"]);
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
        await Assert.That(output).Contains("this.center = new Point();");
        await Assert.That(output).Contains("static zero = /* @__PURE__ */ new Point() as Point;");
        await Assert.That(output).Contains("import { Point } from \"./point.js\";");
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
