namespace Metano.Tests;

public class EnumTranspileTests
{
    [Test]
    public async Task StringEnum_GeneratesUnionType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum Color
            {
                [Name("RED")] Red,
                [Name("GREEN")] Green,
                [Name("BLUE")] Blue,
            }
            """
        );

        await TranspileHelper.AssertGolden("string-enum.ts", result["color.ts"]);
    }

    [Test]
    public async Task StringEnumWithoutNameAttribute_UsesOriginalMemberName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum Priority
            {
                Low,
                Medium,
                High,
            }
            """
        );

        await TranspileHelper.AssertGolden("string-enum-no-aliases.ts", result["priority.ts"]);
    }

    [Test]
    public async Task NumericEnum_GeneratesConstObjectWithUnionAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Priority
            {
                Low,
                Medium,
                High,
            }
            """
        );

        await TranspileHelper.AssertGolden("numeric-enum.ts", result["priority.ts"]);
    }

    /// <summary>
    /// A plain numeric enum is an <c>as const</c> object plus the closed union of its
    /// values (spec 011 FR-044). Every value C# types as the enum but TypeScript
    /// computes as a <c>number</c> (a cast, an operator result) is asserted as the
    /// enum; increments and compound assignments type-check as they are. The golden
    /// is type-checked under <c>tsgo --strict</c> by the runtime's golden typecheck.
    /// </summary>
    [Test]
    public async Task NumericEnum_AssertsEveryComputedValue()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("numeric-enum-const")]
            public enum Color { Red, Green, Blue }

            [Transpile, EmitInFile("numeric-enum-const")]
            public static class Palette
            {
                public static Color Initial = 0;

                public static Color FromNumber(int n) => (Color)n;

                public static Color Literal() => (Color)3;

                public static Color? Maybe(int? n) => (Color?)n;

                public static Color Next(Color c) => c + 1;

                public static Color Combined(Color c) => c | Color.Green;

                public static Color Inverted(Color c) => ~c;

                public static int Distance(Color a, Color b) => a - b;

                public static Color Stepped(Color c)
                {
                    c++;
                    c += 1;

                    return c;
                }

                public static bool LocalStep()
                {
                    var s = Color.Red;
                    s++;

                    return s == Color.Green;
                }

                public static Color Default() => default;

                public static Color Zero() => 0;

                public static Color PassZero() => Identity(0);

                public static Color Identity(Color c) => c;

                public static Color? NullableNext(Color? n)
                {
                    n++;

                    return n;
                }

                public static Color? NullableOr(Color? a, Color b) => a | b;

                public static Color? NullableInverted(Color? n) => ~n;

                public static Color? NullableCompound(Color? n)
                {
                    n += 1;

                    return n;
                }

                public static bool IsBlue(Color c) => c == Color.Blue;

                public static string Label(Color c) =>
                    c switch
                    {
                        Color.Red => "red",
                        Color.Green => "green",
                        _ => "other",
                    };
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "numeric-enum-const.ts",
            result["numeric-enum-const.ts"]
        );
    }

    /// <summary>
    /// A <c>[Flags]</c> enum's alias is the brand <c>number &amp; { readonly __enum?: "E" }</c>
    /// (spec 011 FR-044): bit combinations, <c>0</c> and casts need no assertion, and a
    /// value of another enum is rejected by <c>tsgo --strict</c>.
    /// </summary>
    [Test]
    public async Task FlagsEnum_ComposesWithoutAssertions()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("numeric-enum-flags"), System.Flags]
            public enum Perms { None = 0, Read = 1, Write = 2, Execute = 4 }

            [Transpile, EmitInFile("numeric-enum-flags")]
            public static class Access
            {
                public static Perms Grant()
                {
                    var p = Perms.Read | Perms.Write;
                    p |= Perms.Execute;
                    p &= ~Perms.Read;

                    return p ^ Perms.Write;
                }

                public static bool CanWrite(Perms p) => (p & Perms.Write) != 0;

                public static Perms Zero() => default;

                public static Perms Cast(int n) => (Perms)n;

                public static Perms Three() => (Perms)3;

                public static Perms[] All() => [Perms.Read, Perms.Write, Perms.Execute];

                public static string Describe(Perms p) =>
                    p switch
                    {
                        Perms.None => "none",
                        Perms.Read => "read",
                        _ => "mixed",
                    };
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "numeric-enum-flags.ts",
            result["numeric-enum-flags.ts"]
        );
    }

    /// <summary>
    /// An enum over <c>long</c> / <c>ulong</c> holds exact <c>bigint</c>s (spec 011
    /// FR-044 as amended by #701): its members are <c>bigint</c> literals, its brand
    /// is <c>bigint &amp; { … }</c>, and every operator and conversion lowers as on the
    /// underlying integer, asserted to a plain enum's closed union. The golden is
    /// type-checked under <c>tsgo --strict</c> by the runtime's golden typecheck.
    /// </summary>
    [Test]
    public async Task SixtyFourBitEnums_HoldBigInts()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("numeric-enum-int64"), System.Flags]
            public enum Wide : long { None = 0, Low = 1, High = 1L << 40, Top = 1L << 62 }

            [Transpile, EmitInFile("numeric-enum-int64")]
            public enum Huge : ulong { Zero = 0, Top = 1UL << 63, Max = ulong.MaxValue }

            [Transpile, EmitInFile("numeric-enum-int64")]
            public enum Rank : long { First = 1, Second = 2 }

            [Transpile, EmitInFile("numeric-enum-int64")]
            public enum Small { A = 1, B = 2 }

            [Transpile, EmitInFile("numeric-enum-int64")]
            public static class Ops
            {
                public static Wide Combine() => Wide.Low | Wide.High;

                public static Wide Mask(Wide w) => (w & ~Wide.Low) ^ Wide.Top;

                public static bool HasHigh(Wide w) => (w & Wide.High) != 0;

                public static bool HasTop(Wide w) => w.HasFlag(Wide.Top);

                public static Huge Invert(Huge h) => ~h;

                public static Huge Next(Huge h) => h + 1;

                public static Rank Step(Rank r)
                {
                    r++;
                    r += 2;

                    return r;
                }

                public static long Distance(Rank a, Rank b) => a - b;

                public static Wide Shifted(Wide w) => (Wide)((long)w << 2);

                public static long ToLong(Wide w) => (long)w;

                public static int ToInt(Wide w) => (int)w;

                public static ulong ToULong(Wide w) => (ulong)w;

                public static long FromHuge(Huge h) => (long)h;

                public static double ToDouble(Huge h) => (double)h;

                public static Wide FromLong(long l) => (Wide)l;

                public static Huge FromInt(int i) => (Huge)i;

                public static Small ToSmall(Rank r) => (Small)r;

                public static Rank FromSmall(Small s) => (Rank)s;

                public static Wide Zero() => 0;

                public static Rank NoZero() => default;

                public static Rank[] Ranks() => new Rank[2];

                public static Wide? Lifted(Wide? w) => w | Wide.Low;

                public static bool Before(Huge a) => a < Huge.Max && a != Huge.Top;

                public static string Label(Huge h) =>
                    h switch
                    {
                        Huge.Zero => "zero",
                        Huge.Max => "max",
                        _ => "other",
                    };
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "numeric-enum-int64.ts",
            result["numeric-enum-int64.ts"]
        );
    }

    /// <summary>
    /// A compound assignment and an increment through an extension property of a
    /// plain numeric enum type keep the enum result type: the value the setter
    /// receives is asserted as the enum (spec 011 FR-044).
    /// </summary>
    [Test]
    public async Task EnumExtensionPropertyWrite_AssertsTheStoredValue()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public enum Shade { Light, Dark }

            public class Cell
            {
                public Shade Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public Shade Shade
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Compound(Cell c) { c.Shade += 1; }

                public void Increment(Cell c) { c.Shade++; }
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "enum-extension-property-write.ts",
            result["app/caller.ts"]
        );
    }
}
