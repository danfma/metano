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
