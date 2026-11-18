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
    public async Task NumericEnum_GeneratesEnumDeclaration()
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
}
