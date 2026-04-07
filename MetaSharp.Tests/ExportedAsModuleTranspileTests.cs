namespace MetaSharp.Tests;

public class ExportedAsModuleTranspileTests
{
    [Test]
    public async Task StaticClass_EmitsTopLevelFunctions()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class MathUtils
            {
                public static int Add(int a, int b) => a + b;

                public static string Greet(string name) => $"Hello, {name}!";
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("exported-as-module.ts");
        await Assert.That(result["math-utils.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task StaticClass_NoClassWrapper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Helpers
            {
                public static int Double(int x) => x * 2;
            }
            """
        );

        var output = result["helpers.ts"];
        await Assert.That(output).DoesNotContain("class Helpers");
        await Assert.That(output).Contains("export function");
    }

    [Test]
    public async Task StaticClass_AsyncMethodsWork()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Api
            {
                public static async Task<string> FetchData(string url)
                {
                    return url;
                }
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("exported-as-module-async.ts");
        await Assert.That(result["api.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task StaticClass_IgnoredMembersSkipped()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Utils
            {
                public static int Visible() => 1;

                [Ignore]
                public static int Hidden() => 2;
            }
            """
        );

        var output = result["utils.ts"];
        await Assert.That(output).Contains("visible");
        await Assert.That(output).DoesNotContain("hidden");
    }

    [Test]
    public async Task StaticClass_NameAttributeRenamesFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Ops
            {
                [Name("sum")]
                public static int Add(int a, int b) => a + b;
            }
            """
        );

        var output = result["ops.ts"];
        await Assert.That(output).Contains("export function sum(");
        await Assert.That(output).DoesNotContain("export function add(");
    }

    [Test]
    public async Task StaticClass_NoEqualsHashCodeWith()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Pure
            {
                public static int Identity(int x) => x;
            }
            """
        );

        var output = result["pure.ts"];
        await Assert.That(output).DoesNotContain("equals");
        await Assert.That(output).DoesNotContain("hashCode");
        await Assert.That(output).DoesNotContain("with");
    }
}
