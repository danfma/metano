namespace MetaSharp.Tests;

public class FlagsEnumTests
{
    [Test]
    public async Task FlagsEnum_GeneratesNumericEnum()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [System.Flags]
            public enum FilePermissions
            {
                None = 0,
                Read = 1,
                Write = 2,
                Execute = 4,
                All = Read | Write | Execute
            }
            """
        );

        var output = result["file-permissions.ts"];
        await Assert.That(output).Contains("export enum FilePermissions");
        await Assert.That(output).Contains("None = 0,");
        await Assert.That(output).Contains("Read = 1,");
        await Assert.That(output).Contains("Write = 2,");
        await Assert.That(output).Contains("Execute = 4,");
        await Assert.That(output).Contains("All = 7,");
    }

    [Test]
    public async Task HasFlag_GeneratesBitwiseCheck()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [System.Flags]
            public enum Permissions
            {
                None = 0,
                Read = 1,
                Write = 2
            }

            [Transpile]
            public class Checker
            {
                public bool CanRead(Permissions p) { return p.HasFlag(Permissions.Read); }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("&");
        await Assert.That(output).Contains("===");
        await Assert.That(output).Contains("Permissions.Read");
    }

    [Test]
    public async Task EnumParse_GeneratesIndexAccess()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Color
            {
                Red = 0,
                Green = 1,
                Blue = 2
            }

            [Transpile]
            public class Parser
            {
                public Color Parse(string text) { return System.Enum.Parse<Color>(text); }
            }
            """
        );

        var output = result["parser.ts"];
        await Assert.That(output).Contains("Color[text as keyof typeof Color]");
    }
}
