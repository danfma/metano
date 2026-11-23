namespace Metano.Tests;

/// <summary>
/// Which static methods a module-shaped static class emits and how calls into it are
/// spelled: a private helper is emitted, unexported, beside the functions that call it
/// (#609), and a static class that emits as a module because it holds extension
/// members flattens its qualified calls and their imports like a <c>[NoContainer]</c>
/// one (#554).
/// </summary>
public class ModuleFunctionEmissionTests
{
    [Test]
    public async Task PrivateHelperOfANoContainerClass_IsEmittedUnexported()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Helpers
            {
                private static long Twice(long l) => l * 2;
                public static long Use(long l) => Twice(l);
            }
            """
        );

        var module = files["helpers.ts"];

        await Assert.That(module).StartsWith("function twice(l: bigint): bigint {");
        await Assert.That(module).Contains("return twice(l);");
        await Assert.That(files["index.ts"]).IsEqualTo("export { use } from \"./helpers\";\n");
    }

    [Test]
    public async Task PrivateOverloadGroupOfANoContainerClass_EmitsItsFastPathsUnexported()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Host
            {
                private static int Over(int x) => x;
                private static int Over(string s) => 1;
                public static int Go() => Over(1) + Over("a");
            }
            """
        );

        var module = files["host.ts"];

        await Assert.That(module).Contains("\nfunction overX(x: number): number {");
        await Assert.That(module).Contains("\nfunction overS(s: string): number {");
        await Assert.That(module).Contains("return (overX(1) + overS(\"a\")) | 0;");
    }

    [Test]
    public async Task QualifiedCallIntoAnExtensionContainer_FlattensAndImportsTheFunction()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class IgnHost
            {
                [Ignore] public static int Twice(this int x) => x * 2;
                public static int Plain(int x) => x;
                public static int Plain(string x) => 0;
                internal static int Inner(int x) => x;
            }

            [Transpile]
            public static class OnlyBlock
            {
                extension(string s) { public int Len() => s.Length; }
                public static int Solo(int x) => x;
            }

            [Transpile]
            public class Use
            {
                public int Go() =>
                    IgnHost.Plain(1) + IgnHost.Plain("y") + IgnHost.Inner(2) + OnlyBlock.Solo(3);
            }
            """
        );

        var use = files["use.ts"];

        await Assert
            .That(use)
            .Contains("import { inner, plainInt32, plainString } from \"./ign-host\";");

        await Assert.That(use).Contains("import { solo } from \"./only-block\";");

        await Assert
            .That(use)
            .Contains("return (plainInt32(1) + plainString(\"y\") + inner(2) + solo(3)) | 0;");
    }

    [Test]
    public async Task ExtensionCalledHelperOfItsOwnContainer_IsCalledByItsBareName()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Ext
            {
                public static long Sq(this long x) => Helper(x);
                private static long Helper(long x) => x * x;
            }
            """
        );

        var module = files["ext.ts"];

        await Assert.That(module).Contains("return helper(x);");
        await Assert.That(module).Contains("\nfunction helper(x: bigint): bigint {");
    }

    [Test]
    public async Task CrossPackageCallIntoAnExtensionContainer_ImportsTheFunctionFromThePackage()
    {
        var files = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public static class IgnHostL
            {
                [Ignore] public static int Twice(this int x) => x * 2;
                public static int Plain(int x) => x;
            }
            """,
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace App;

            public class Use { public int Go() => Lib.IgnHostL.Plain(1); }
            """
        );

        var use = files["app/use.ts"];

        await Assert.That(use).Contains("import { plain } from \"lib/lib\";");
        await Assert.That(use).Contains("return plain(1);");
        await Assert.That(use).DoesNotContain("IgnHostL");
    }
}
