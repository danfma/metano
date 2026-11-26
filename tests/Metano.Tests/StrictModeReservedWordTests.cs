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
    public async Task Dart_MembersNamedLikeReservedWords_AreEscapedAtTheDeclarationAndEveryUse()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record KW(int In, int New, int Of);

            [Transpile]
            public class KwUse
            {
                public int Default { get; set; }
                public static int Sum(KW k) => k.In + k.New + k.Of;
                public static KW With(KW k) => k with { In = 3 };
                public int Read() => Default + this.Default;
            }
            """
        );

        var record = files["k_w.dart"];
        var use = files["kw_use.dart"];

        await Assert.That(record).Contains("KW(this.in_, this.new_, this.of);");
        await Assert.That(record).Contains("other.in_ == this.in_");
        await Assert.That(record).DoesNotContain("this.in ");
        await Assert.That(record).DoesNotContain("this.in,");
        await Assert.That(use).Contains("k.in_ + k.new_ + k.of");
        await Assert.That(use).Contains("k.copyWith(in_: 3)");
        await Assert.That(use).Contains("late int default_;");
        await Assert.That(use).Contains("this.default_ + this.default_");
        await Assert.That(use).Contains("static KW with_(KW k)");
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

    [Test]
    public async Task StaticDispatcher_OfAnEscapedOrRenamedClass_ReceivesItsEmittedName()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class let
            {
                public static string Make(int n) => "n" + n;
                public static string Make(string s) => "s" + s;
            }

            [Transpile, Name("Signals")]
            public static class Signal
            {
                public static string Describe(int x) => "int" + x;
                public static string Describe(string x) => "str" + x;
            }

            [Transpile]
            public class Signal<T> { public T Value; public Signal(T v) { Value = v; } }
            """
        );

        await Assert.That(files["let_.ts"]).Contains("return let_.makeN(args[0] as number);");
        await Assert.That(files["let_.ts"]).DoesNotContain("return let.");
        await Assert
            .That(files["signals.ts"])
            .Contains("return Signals.describeInt32(args[0] as number);");
    }

    [Test]
    public async Task ConstructorSelectors_OfAnEscapedClass_AgreeAcrossPackages()
    {
        const string library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("plib")]

            namespace Lib;

            public class let
            {
                public string K = "p";
                public let(System.Func<int> f) { K = "fi" + f(); }
                [Name("Lbl")] public let(System.Func<string> f) { K = "fs" + f(); }
                public let(int n) { K = "n" + n; }
            }
            """;
        const string consumer = """
            using Lib;

            [Transpile]
            public class AppLet : let
            {
                public int Z = 1;
                public AppLet(int n) : base(() => "a" + n) { Z += n; }
                public AppLet(string s) : base(() => s.Length) { }
            }
            """;

        var producer = TranspileHelper.Transpile(library);
        var viaSource = TranspileHelper.TranspileWithLibrary(library, consumer)["app-let.ts"];
        var viaImage = TranspileHelper.TranspileWithLibraryImage(library, consumer)["app-let.ts"];

        await Assert
            .That(string.Join("\n", producer.Values))
            .Contains("static readonly bylet_Lbl = ");
        await Assert.That(viaSource).Contains("super(let_.bylet_Lbl, ");
        await Assert.That(viaSource).DoesNotContain("let_.byletLbl");
        await Assert.That(viaImage).Contains("super(let_.bylet_Lbl, ");
        await Assert.That(viaImage).DoesNotContain("let_.byletLbl");
    }
}
