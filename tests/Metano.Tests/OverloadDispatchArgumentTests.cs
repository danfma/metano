namespace Metano.Tests;

/// <summary>
/// Overload dispatchers with optional and <c>params</c> parameters (#438): the
/// overload signatures mark a parameter with a default optional, a branch
/// accepts the omitted arguments and applies the default, and a
/// <c>params</c> branch takes the expanded trailing arguments a call site
/// emits (an explicit C# array is spread).
/// </summary>
public class OverloadDispatchArgumentTests
{
    [Test]
    public async Task Constructor_DefaultsAndParams_AcceptOmittedAndExpandedArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Opt
            {
                public string Log { get; } = "";

                public Opt(int a = 5) { Log = "int:" + a; }

                public Opt(string s, params int[] rest) { Log = "params:" + s + ":" + rest.Length; }
            }
            """
        );

        await Assert
            .That(result["opt.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-defaults-params-constructor.ts"));
    }

    [Test]
    public async Task Methods_DefaultsAndParams_LeaveDefaultsToTheFastPath()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Ops
            {
                public string M(int a = 5) => "int:" + a;

                public string M(string s, params int[] rest) => "params:" + s + ":" + rest.Length;

                public static string S(int a = 5, string b = "x") => "S:" + a + b;

                public static string S(bool flag) => "bool";
            }
            """
        );

        await Assert
            .That(result["ops.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-defaults-params-methods.ts"));
    }

    [Test]
    public async Task BrandedCompanion_DefaultsAndParams_DispatchNamespaceFunctions()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Meters(int Value)
            {
                public static Meters Of(int a = 5) => new(a);

                public static Meters Of(string s, params int[] rest) => new(rest.Length);
            }
            """
        );

        await Assert
            .That(result["meters.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-defaults-params-branded.ts"));
    }

    [Test]
    public async Task Order_TriesExactArityBeforeDefaultsAndNormalFormBeforeParams()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Over
            {
                public string F(int a, int b = 7) => "ab" + b;

                public string F(int a) => "a";

                public string Many(params string[] xs) => "many" + xs.Length;

                public string Many(string a, string b) => "two";
            }
            """
        );

        await Assert
            .That(result["over.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-defaults-params-order.ts"));
    }

    [Test]
    public async Task Defaults_ReferencingValues_ImportThemAndKeepTheBclType()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public enum Color { Red, Green }

            public class Painter
            {
                public string Log { get; } = "";

                public Painter(Color c = Color.Green) { Log = "color:" + (int)c; }

                public Painter(bool flag) { Log = "bool"; }

                public string D(Color c = Color.Green) => "color:" + (int)c;

                public string D(bool b) => "bool";

                public static string F(int a, decimal d = 1.5m) => "dec:" + d;

                public static string F(string s) => "s";
            }
            """
        );

        await Assert
            .That(result["painter.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-defaults-imports.ts"));
    }

    [Test]
    public async Task SingleMethod_EnumDefault_ImportsTheEnumAsAValue()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public enum Color { Red, Green }

            public class Single
            {
                public string D(Color c = Color.Green) => "c" + (int)c;
            }
            """
        );

        var output = result["single.ts"];

        await Assert.That(output).Contains("import { Color } from \"./color\";");
        await Assert.That(output).DoesNotContain("import type { Color }");
    }

    [Test]
    public async Task OptionalAttribute_WithoutDefault_AcceptsTheOmittedArgument()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;
            [assembly: TranspileAssembly]

            public class Tagger
            {
                public string Tag([Optional] string? label) => label ?? "none";

                public string Tag(int count, int extra) => "pair";
            }
            """
        );

        await Assert
            .That(result["tagger.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("dispatcher-optional-attribute.ts"));
    }

    [Test]
    public async Task CallSites_OmitDefaultsAndPassParamsExpanded()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Opt
            {
                public Opt(int a = 5) { }

                public Opt(string s, params int[] rest) { }
            }

            public static class Use
            {
                public static void Run()
                {
                    var none = new Opt();
                    var one = new Opt("s");
                    var expanded = new Opt("s", 1, 2);
                    var array = new Opt("s", new[] { 1, 2, 3 });
                }
            }
            """
        );

        var output = result["use.ts"];

        await Assert.That(output).Contains("new Opt()");
        await Assert.That(output).Contains("new Opt(\"s\")");
        await Assert.That(output).Contains("new Opt(\"s\", 1, 2)");
        await Assert.That(output).Contains("new Opt(\"s\", ...[1, 2, 3])");
    }
}
