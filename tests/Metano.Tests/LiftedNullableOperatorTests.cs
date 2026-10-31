namespace Metano.Tests;

/// <summary>
/// C# lifted operators over nullable value types (#287): arithmetic yields
/// <c>null</c> and a relational comparison yields <c>false</c> for a
/// <c>null</c> operand, string concatenation and interpolation format
/// <c>null</c> as <c>""</c>, and <c>Nullable&lt;T&gt;</c>'s members lower to
/// null tests. Every expected shape was checked with <c>tsgo --strict</c> and
/// run under Bun against the C# results.
/// </summary>
public class LiftedNullableOperatorTests
{
    [Test]
    public async Task Arithmetic_OnNullableOperand_YieldsNullWhenNull()
    {
        var output = TranspileInHelperClass(
            """
            public static int? AddQty(Order o, int k) => o.Qty + k;
            public static int? MulBoth(int? a, int? b) => a * b;
            public static int? Shift(int? a) => a << 2;
            public static double? Mixed(int? a, double b) => a * b;
            """
        );

        await Assert.That(output).Contains("return o.qty == null ? null : o.qty + k;");
        await Assert.That(output).Contains("return a == null || b == null ? null : a * b;");
        await Assert.That(output).Contains("return a == null ? null : a << 2;");
        await Assert.That(output).Contains("return a == null ? null : a * b;");
    }

    [Test]
    public async Task NonNullableOperands_KeepThePlainOperator()
    {
        var output = TranspileInHelperClass(
            """
            public static int Add(int a, int b) => a + b;
            public static bool Less(int a, int b) => a < b;
            """
        );

        await Assert.That(output).Contains("return a + b;");
        await Assert.That(output).Contains("return a < b;");
    }

    [Test]
    public async Task Relational_OnNullableOperand_IsFalseWhenNull()
    {
        var output = TranspileInHelperClass(
            """
            public static bool Less(Order o) => o.Qty < 5;
            public static bool BothLess(int? a, int? b) => a < b;
            public static bool High(Level? level) => level >= Level.High;
            """
        );

        await Assert.That(output).Contains("return o.qty != null && o.qty < 5;");
        await Assert.That(output).Contains("return a != null && b != null && a < b;");
        await Assert.That(output).Contains("return level != null && level >= Level.High;");
    }

    [Test]
    public async Task Equality_KeepsItsOrdinaryLowering()
    {
        var output = TranspileInHelperClass(
            """
            public static bool Five(Order o) => o.Qty == 5;
            public static bool Missing(Order o) => o.Qty == null;
            """
        );

        await Assert.That(output).Contains("return o.qty === 5;");
        await Assert.That(output).Contains("return o.qty == null;");
    }

    [Test]
    public async Task NullConditionalOperand_MovesTheOperatorIntoTheGuard()
    {
        var output = TranspileInHelperClass(
            """
            public static int Sum(Order? o) => o?.Items.Count + 1 ?? 0;
            public static int? Ahead(Order? o) => 1 + o?.Items.Count;
            public static int? NextQty(Order? o) => o?.Qty + 1;
            public static bool Many(Order? o) => o?.Items.Count > 1;
            public static int? Negated(Order? o) => -o?.Qty;
            public static int? AheadOf(int? k, Order? o) => k + o?.Items.Count;
            public static bool ManyOr(Order? o, bool flag) => o?.Items.Count > 1 || flag;
            """
        );

        await Assert.That(output).Contains("return (o == null ? null : o.items.length + 1) ?? 0;");
        await Assert.That(output).Contains("return o == null ? null : 1 + o.items.length;");
        await Assert
            .That(output)
            .Contains("return o == null ? null : o.qty == null ? null : o.qty + 1;");
        await Assert
            .That(output)
            .Contains("return (o == null ? null : o.items.length > 1) ?? false;");
        await Assert
            .That(output)
            .Contains("return o == null ? null : o.qty == null ? null : -o.qty;");
        await Assert
            .That(output)
            .Contains("return o == null ? null : k == null ? null : k + o.items.length;");
        await Assert
            .That(output)
            .Contains("return ((o == null ? null : o.items.length > 1) ?? false) || flag;");
    }

    [Test]
    public async Task SideEffectingOperands_AreEvaluatedOnceInSourceOrder()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Twice(Order o) => o.NextQty() + o.NextQty();
            public static int? Later(int? a, Order o) => a + o.Count();
            public static int? AfterCall(Order o, Order? p) => o.NextQty() + p?.Qty;
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => ((receiver$temp$1) => receiver$temp$0 == null || receiver$temp$1 == null ? null : receiver$temp$0 + receiver$temp$1)(o.nextQty()))(o.nextQty());"
            );

        // The left operand is bound too, so it is read before the call runs.
        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => ((receiver$temp$1) => receiver$temp$0 == null ? null : receiver$temp$0 + receiver$temp$1)(o.count()))(a);"
            );

        // A side-effecting left operand keeps the access on the right from
        // moving it into its guard: both are bound, in source order.
        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => ((receiver$temp$1) => receiver$temp$0 == null || receiver$temp$1 == null ? null : receiver$temp$0 + receiver$temp$1)(p?.qty))(o.nextQty());"
            );
    }

    [Test]
    public async Task AwaitedOperand_IsBoundInsideTheAsyncFunction()
    {
        var output = TranspileInHelperClass(
            "public static async Task<int?> Plus(Task<int?> t) => await t + 1;"
        );

        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => receiver$temp$0 == null ? null : receiver$temp$0 + 1)(await t);"
            );
    }

    [Test]
    public async Task NullLiteralOperand_FoldsToNull()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Plus(int? a) => a + null;
            public static bool Less(int? a) => a < null;
            """
        );

        await Assert
            .That(output)
            .Contains("export function plus(a: number | null): number | null {\n  return null;");
        await Assert
            .That(output)
            .Contains("export function less(a: number | null): boolean {\n  return false;");
    }

    [Test]
    public async Task UnaryAndCompound_AreLifted()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Negate(int? a) => -a;
            public static bool? Not(bool? b) => !b;
            public static int? Bump(Order o)
            {
                o.Qty += 10;
                return o.Qty;
            }
            public static int? Halve(int? x)
            {
                x /= 2;
                return x;
            }
            public static int? Quotient(int? a, int b) => a / b;
            """
        );

        await Assert.That(output).Contains("return a == null ? null : -a;");
        await Assert.That(output).Contains("return b == null ? null : !b;");
        await Assert.That(output).Contains("o.qty = o.qty == null ? null : o.qty + 10;");

        // A lifted integer division keeps its truncating lowering.
        await Assert.That(output).Contains("x = x == null ? null : intDiv(x, 2);");
        await Assert.That(output).Contains("return a == null ? null : intDiv(a, b);");
    }

    [Test]
    public async Task CompoundOnSideEffectingTarget_IsNotLifted()
    {
        var output = TranspileInHelperClass(
            """
            public static void Bump(Order o)
            {
                o.Self().Qty += 1;
            }
            """
        );

        await Assert.That(output).Contains("o.self().qty += 1;");
    }

    [Test]
    public async Task BooleanLogicalOperators_KeepThreeValuedLowering()
    {
        var output = TranspileInHelperClass("public static bool? Both(bool? a, bool? b) => a & b;");

        await Assert.That(output).Contains("return a & b;");
    }

    [Test]
    public async Task Concatenation_FormatsNullAsEmptyString()
    {
        var output = TranspileInHelperClass(
            """
            public static string Count(Order? o) => "n" + o?.Items.Count;
            public static string Trailing(Order o) => o.Qty + "!";
            public static string Name(string? s) => "s=" + s;
            public static string Known(string s, int k) => s + k;
            """
        );

        await Assert.That(output).Contains("return \"n\" + (o?.items.length ?? \"\");");
        await Assert.That(output).Contains("return (o.qty ?? \"\") + \"!\";");
        await Assert.That(output).Contains("return \"s=\" + (s ?? \"\");");
        await Assert.That(output).Contains("return s + k;");
    }

    [Test]
    public async Task CompoundConcatenation_FormatsNullAsEmptyString()
    {
        var output = TranspileInHelperClass(
            """
            public static string Append(string? s, int? n)
            {
                s += n;
                s += "!";
                return s;
            }
            """
        );

        await Assert.That(output).Contains("s = (s ?? \"\") + (n ?? \"\");");
        await Assert.That(output).Contains("s += \"!\";");
    }

    [Test]
    public async Task Interpolation_FormatsNullAsEmptyString()
    {
        var output = TranspileInHelperClass(
            "public static string Show(Order o, string? s) => $\"q={o.Qty};{s}\";"
        );

        await Assert.That(output).Contains("return `q=${o.qty ?? \"\"};${s ?? \"\"}`;");
    }

    [Test]
    public async Task InterpolationWithFormatClause_StillFormatsNullAsEmptyString()
    {
        var output = TranspileInHelperClass(
            "public static string Show(Order o) => $\"q={o.Qty:D2}\";"
        );

        await Assert.That(output).Contains("return `q=${o.qty ?? \"\"}`;");
    }

    [Test]
    public async Task Concatenation_OfMaybeNullTypeParameter_FormatsNullAsEmptyString()
    {
        var output = TranspileInHelperClass(
            "public static string Show<T>(T value) => \"v=\" + value;"
        );

        await Assert.That(output).Contains("return \"v=\" + (value ?? \"\");");
    }

    [Test]
    public async Task Concatenation_InNullableDisabledContext_IsLeftAsIs()
    {
        var output = TranspileInHelperClass(
            """
            #nullable disable
            public static string Show(string s) => "s=" + s;
            #nullable restore
            """
        );

        await Assert.That(output).Contains("return \"s=\" + s;");
    }

    [Test]
    public async Task HasValue_LowersToNullTest()
    {
        var output = TranspileInHelperClass(
            """
            public static bool Has(Order o) => o.Qty.HasValue;
            public static bool? NextHas(Order? o) => o?.Qty.HasValue;
            """
        );

        await Assert.That(output).Contains("return o.qty != null;");
        await Assert.That(output).Contains("return o == null ? null : o.qty != null;");
    }

    [Test]
    public async Task Value_IsTheValueWhenProvenAndThrowsOtherwise()
    {
        var output = TranspileInHelperClass(
            """
            public static bool Guarded(int? x) => x.HasValue && x.Value > 3;
            #pragma warning disable CS8629
            public static int Unchecked(Order o) => o.Qty.Value;
            #pragma warning restore CS8629
            """
        );

        await Assert.That(output).Contains("return x != null && x > 3;");
        await Assert
            .That(output)
            .Contains(
                "return o.qty ?? (() => {\n    throw new Error(\"Nullable object must have a value.\");\n  })();"
            );
    }

    [Test]
    public async Task StructMembersNamedValueOrHasValue_AreOrdinaryReads()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Amount(Money? m) => m?.Value;
            public static bool? Known(Money? m) => m?.HasValue;
            """
        );

        await Assert.That(output).Contains("return m?.value;");
        await Assert.That(output).Contains("return m?.hasValue;");
    }

    [Test]
    public async Task Value_InNullableDisabledContext_IsLeftUnguarded()
    {
        var output = TranspileInHelperClass(
            """
            #nullable disable
            public static int Unchecked(Order o) => o.Qty.Value;
            #nullable restore
            """
        );

        await Assert.That(output).Contains("return o.qty;");
    }

    [Test]
    public async Task GetValueOrDefault_FallsBackToTheDefault()
    {
        var output = TranspileInHelperClass(
            """
            public static int OrZero(Order o) => o.Qty.GetValueOrDefault();
            public static int OrSeven(Order o) => o.Qty.GetValueOrDefault(7);
            public static Level OrLow(Level? level) => level.GetValueOrDefault();
            public static int OrCount(int? a, Order o) => a.GetValueOrDefault(o.Count());
            public static int ChainOr(Order? o) => o?.Qty.GetValueOrDefault() ?? -1;
            """
        );

        await Assert.That(output).Contains("return o.qty ?? 0;");
        await Assert.That(output).Contains("return o.qty ?? 7;");
        await Assert.That(output).Contains("return level ?? Level.Low;");

        // The argument is evaluated even when `a` has a value, as in C#.
        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => ((receiver$temp$1) => receiver$temp$0 ?? receiver$temp$1)(o.count()))(a);"
            );
        await Assert.That(output).Contains("return (o == null ? null : o.qty ?? 0) ?? -1;");
    }

    [Test]
    public async Task GetValueOrDefault_OnTypeParameter_ReportsUnsupported()
    {
        var output = TranspileInHelperClass(
            "public static T OrDefault<T>(T? value) where T : struct => value.GetValueOrDefault();"
        );

        await Assert.That(output).Contains("Unsupported expression: NullableGetValueOrDefault");
    }

    [Test]
    public async Task LiftedDecimalAndTemporalOperators_GuardTheirMethodLowering()
    {
        var output = TranspileInHelperClass(
            """
            public static decimal? Total(decimal? a, decimal b) => a + b;
            public static decimal? Negative(decimal? a) => -a;
            public static bool Before(System.DateOnly? a, System.DateOnly b) => a < b;
            public static bool Same(decimal? a) => a == null;
            public static decimal? Raise(Order o)
            {
                o.Price += 1m;
                return o.Price;
            }
            """
        );

        await Assert.That(output).Contains("return a == null ? null : a.plus(b);");
        await Assert.That(output).Contains("return a == null ? null : a.neg();");
        await Assert
            .That(output)
            .Contains("return a != null && Temporal.PlainDate.compare(a, b) < 0;");
        await Assert.That(output).Contains("return a == null;");
        await Assert
            .That(output)
            .Contains("o.price = o.price == null ? null : o.price.plus(new Decimal(\"1\"));");
    }

    [Test]
    public async Task NullableArrayElement_IsParenthesized()
    {
        var output = TranspileInHelperClass("public static List<Order?> Orders() => [null];");

        await Assert.That(output).Contains("export function orders(): (Order | null)[] {");
    }

    [Test]
    public async Task LiftedOperators_MatchGolden()
    {
        var output = TranspileInHelperClass(
            """
            public static string ConcatCount(Order? o) => "n" + o?.Items.Count;
            public static int CoalesceSum(Order? o) => o?.Items.Count + 1 ?? 0;
            public static int? AddQty(Order o, int k) => o.Qty + k;
            public static bool LessQty(Order o) => o.Qty < 5;
            public static bool HasQty(Order o) => o.Qty.HasValue;
            public static int QtyOrSeven(Order o) => o.Qty.GetValueOrDefault(7);
            public static int? Twice(Order o) => o.NextQty() + o.NextQty();
            public static int? NestedPlus(Order? o) => o?.Next?.Qty + 1;
            public static string Show(Order o) => $"q={o.Qty}";
            public static List<Order?> Orders() => [null];
            """
        );

        await Assert
            .That(output)
            .IsEqualTo(TranspileHelper.ReadExpected("lifted-nullable-operators.ts"));
    }

    private static string TranspileInHelperClass(string members) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Metano.Annotations;

            [Transpile]
            public enum Level { Low, High }

            [Transpile]
            public readonly record struct Money(int Value, bool HasValue);

            [Transpile]
            public class Order
            {
                public List<int> Items { get; } = [];
                public int? Qty { get; set; }
                public decimal? Price { get; set; }
                public Order Self() => this;
                public Order? Next { get; set; }
                public int? NextQty() => Qty;
                public int Count() => Items.Count;
            }

            [Transpile, NoContainer]
            public static class Helper
            {
            {{members}}
            }
            """
        )["helper.ts"];
}
