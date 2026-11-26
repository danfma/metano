namespace Metano.Tests;

/// <summary>
/// ES modules are strict mode, so <c>static</c>, <c>package</c>, <c>public</c>,
/// <c>interface</c> and the like cannot name a local, parameter, label or
/// top-level function (#791); a member keeps the name, which JavaScript accepts
/// after a dot. Dart has the same split for its own reserved words.
/// </summary>
public class StrictModeReservedWordTests
{
    [Test]
    public async Task IdentifierPositions_EscapeStrictModeWords()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Probes
            {
                public static int Words(int @static, int package)
                {
                    @interface: while (true) { break @interface; }
                    var @public = 1;
                    var @private = @public + package + @static;
                    var arguments = @private;
                    return arguments;
                }
            }
            """
        )["probes.ts"];

        await Assert.That(output).Contains("words(static_: number, package_: number)");
        await Assert.That(output).Contains("$interface: while (true) {");
        await Assert.That(output).Contains("break $interface;");
        await Assert.That(output).Contains("const public_ = 1;");
        await Assert.That(output).Contains("const private_ = (public_ + package_ + static_) | 0;");
        await Assert.That(output).Contains("const arguments_ = private_;");
    }

    [Test]
    public async Task ModuleFunctions_EscapeStrictModeWords_AtDeclarationAndCall()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Mod
            {
                public static int Package(int a) => a;
                public static int Use() => Package(1);
            }
            """
        )["mod.ts"];

        await Assert.That(output).Contains("export function package_(a: number)");
        await Assert.That(output).Contains("return package_(1);");
    }

    [Test]
    public async Task Members_KeepStrictModeWordsAsNames()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Inst
            {
                public int Public() => 1;
                public int Static { get; set; }
                public int Use() => Public() + Static;
            }
            """
        )["inst.ts"];

        await Assert.That(output).Contains("public(): number");
        await Assert.That(output).Contains("declare static: number;");
        await Assert.That(output).Contains("this.static = 0;");
        await Assert.That(output).Contains("this.public() + this.static");
    }

    [Test]
    public async Task Dart_IdentifierPositions_EscapeReservedWords_AndMembersKeepTheirNames()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Probe
            {
                public int Of(int @final, int @var)
                {
                    var @with = @final + @var;
                    var @assert = @with;
                    return @assert;
                }
            }
            """
        );

        var output = files["probe.dart"];

        await Assert.That(output).Contains("int of(int final_, int var_)");
        await Assert.That(output).Contains("var with_ = final_ + var_;");
        await Assert.That(output).Contains("assert_");
    }

    [Test]
    public async Task EscapedFunctions_AreImportedByAnotherFile_AndThroughTheBarrel()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Words
            {
                public static string Package(string @let) => @let + "!";
                public static string Static(this string s) => s + "?";
            }

            [Transpile]
            public static class Caller
            {
                public static string Run() => Words.Package("k") + "k".Static();
            }
            """
        );

        await Assert
            .That(files["caller.ts"])
            .Contains("import { package_, static_ } from \"./words.js\";");

        await Assert.That(files["caller.ts"]).Contains("package_(\"k\")");
        await Assert.That(files["index.ts"]).Contains("package_");
    }

    [Test]
    public async Task EscapedLocal_AndEscapedFunctionOfTheSameWord_DoNotCollide()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Words
            {
                public static string Package(string s) => s + "!";
                public static string Helper(string s) => s + "!";
            }

            [Transpile]
            public static class Caller
            {
                public static string Run()
                {
                    var helper = "v";
                    var @package = "v";

                    return helper + Words.Helper("k") + @package + Words.Package("k");
                }
            }
            """
        );

        await Assert.That(files["caller.ts"]).DoesNotContain("const package_ =");
    }

    [Test]
    public async Task ClassNamedLikeAStrictModeWord_IsEscapedEverywhere()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class @implements { public int V = 1; }

            [Transpile]
            public static class Use
            {
                public static int Run() => new @implements().V;
            }
            """
        );

        var all = string.Join("\n", files.Values);

        await Assert.That(all).Contains("class implements_");
        await Assert.That(all).Contains("new implements_()");
        await Assert.That(all).DoesNotContain("class implements ");
        await Assert.That(files["use.ts"]).Contains("import { implements_ } from");
    }
}
