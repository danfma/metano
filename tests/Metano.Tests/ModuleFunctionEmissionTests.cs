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
        await Assert.That(files["index.ts"]).IsEqualTo("export { use } from \"./helpers.js\";\n");
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
            .Contains("import { inner, plainInt32, plainString } from \"./ign-host.js\";");

        await Assert.That(use).Contains("import { solo } from \"./only-block.js\";");

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
    public async Task OverloadedExtensionBlockMember_ReportsMS0001InsteadOfTwoFunctions()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class BlockHost
            {
                extension(string s)
                {
                    public int Twice() => s.Length * 2;
                    public int Twice(int k) => s.Length * k;
                }
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == "MS0001");

        await Assert
            .That(error.Message)
            .Contains("'BlockHost' emits 2 module functions named 'twice'");

        await Assert.That(files.ContainsKey("block-host.ts")).IsFalse();
    }

    [Test]
    public async Task GenericExtensionBlockMember_DeclaresTheBlockTypeParameters()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Mod
            {
                extension<T>(T[] items) { public T Peek() => items[0]; }
            }
            """
        );

        await Assert.That(files["mod.ts"]).Contains("export function peek<T>(items: T[]): T {");
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

    [Test]
    public async Task MethodGroupOfABclExtensionContainer_KeepsItsClassQualifier()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile, NoContainer]
            public static class Nc
            {
                public static IEnumerable<int> Range(int a, int b) => new[] { -1 };
            }

            [Transpile]
            public class Use
            {
                public Func<int, int, IEnumerable<int>> Group() => Enumerable.Range;
            }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).DoesNotContain("from \"./nc");
        await Assert.That(use).DoesNotContain("return range;");
    }

    [Test]
    public async Task ImportBoundClassWithAnExtension_KeepsItsQualifiedImport()
    {
        var files = TranspileHelper.Transpile(
            """
            [Import("Lib", "./lib-mod")]
            public static class Lib
            {
                public static int Foo() => 0;
                public static int Twice(this int x) => 0;
            }

            [Transpile]
            public class Use { public int Go() => Lib.Foo(); }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("import { Lib } from \"./lib-mod\";");
        await Assert.That(use).Contains("return Lib.foo();");
    }

    [Test]
    public async Task ExternalClassWithAnExtension_KeepsItsQualifier()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public static class JSON2
            {
                public static string Stringify(int x) => "";
                public static int Twice(this int x) => 0;
            }

            [Transpile]
            public class Use { public string Go() => JSON2.Stringify(1); }
            """
        );

        await Assert.That(files["use.ts"]).Contains("return JSON2.stringify(1);");
    }

    [Test]
    public async Task ExtensionContainerStatic_SharingANoContainerFunctionName_StaysValid()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace P.A
            {
                [Transpile]
                public static class StrExt
                {
                    public static string Shout(this string s) => s.ToUpper();
                    public static int Make(int x) => x;
                }
            }

            namespace P
            {
                [Transpile, NoContainer]
                public static class Nc { public static int Make(int x) => x * 10; }

                [Transpile]
                public class Use { public int Go() => Nc.Make(2); }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await Assert.That(files["p/use.ts"]).Contains("import { make } from \"./nc.js\";");
    }

    [Test]
    public async Task TwoModulesMembersOfOneNameReadInOneFile_ReportInsteadOfBindingOne()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace P.A
            {
                [Transpile]
                public static class StrExt
                {
                    public static string Shout(this string s) => s.ToUpper();
                    public static int Make(int x) => x;
                }
            }

            namespace P
            {
                [Transpile, NoContainer]
                public static class Nc { public static int Make(int x) => x * 10; }

                [Transpile]
                public class Use { public int Go() => Nc.Make(2) + P.A.StrExt.Make(3); }
            }
            """
        );

        var clash = diagnostics.Single(d => d.Code == "MS0021");

        await Assert.That(clash.Message).Contains("'make' of StrExt is read in a file");
    }

    [Test]
    public async Task LocalNamedLikeACalledModuleFunction_IsRenamed()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Ext
            {
                public static int Double(this int x) => x * 2;
                public static int Helper(int x) => x + 100;
            }

            [Transpile]
            public class Use
            {
                public int Go()
                {
                    var helper = 1;
                    return Ext.Helper(helper);
                }
            }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("const helper$1 = 1;");
        await Assert.That(use).Contains("return helper(helper$1);");
    }

    [Test]
    public async Task PrivateHelperNamedLikeAnotherModulesExport_IsNotImportedOverItself()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class A
            {
                private static int Helper(int v) => v + 1;
                public static int Use(int v) => Helper(v);
            }

            [Transpile, NoContainer]
            public static class B { public static int Helper(int v) => v * 2; }
            """
        );

        var module = files["a.ts"];

        await Assert.That(module).DoesNotContain("import");
        await Assert.That(module).Contains("return helper(v);");
    }

    [Test]
    public async Task TwoExtensionContainersWithASameNamedStatic_ReportNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace P.A
            {
                [Transpile]
                public static class AExt
                {
                    public static int Twice(this int x) => x * 2;
                    public static int Helper(int x) => x;
                }
            }

            namespace P.B
            {
                [Transpile]
                public static class BExt
                {
                    public static int Thrice(this int x) => x * 3;
                    public static int Helper(int x) => x + 1;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }
}
