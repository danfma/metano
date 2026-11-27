namespace Metano.Tests;

/// <summary>
/// A <c>params</c> argument in normal form (the argument is the array itself) spreads into the
/// TypeScript rest parameter, however the call spells it: named, positional, reordered, in a
/// constructor, an instance, static or extension method, or a base-constructor call (#829).
/// </summary>
public class ParamsNormalFormTests
{
    private const string Source = """
        namespace App;

        [Transpile]
        public sealed class Part { }

        [Transpile]
        public class Card
        {
            public Card(string title, string? sub = null, Action? onClose = null, params Part[] content) { }

            public int Count(string label, int pad = 0, params Part[] items) => items.Length;

            public static int Total(string label, int pad = 0, params Part[] items) => items.Length;
        }

        [Transpile]
        public static class CardExtensions
        {
            public static int Tally(this Card card, int pad = 0, params Part[] items) => items.Length;
        }

        [Transpile]
        public class Fancy : Card
        {
            public Fancy(Part[] parts) : base("t", content: parts) { }
        }

        [Transpile]
        public class Fancier : Card
        {
            public Fancier(Part[] parts) : base("t", null, null, parts) { }
        }
        """;

    private static string Emit(string caller)
    {
        var result = TranspileHelper.Transpile(
            Source
                + $$"""

                [Transpile]
                public sealed class Caller
                {
                    private static Part P() => new Part();

                    {{caller}}
                }
                """
        );

        return result["app/caller.ts"];
    }

    [Test]
    public async Task Constructor_NamedArrayArgument_IsSpread()
    {
        var output = Emit(
            "public Card Make(Part[] arr) => new Card(\"t\", onClose: () => { }, content: arr);"
        );

        await Assert.That(output).Contains("...arr)");
        await Assert.That(output).DoesNotContain(", arr)");
    }

    [Test]
    public async Task Constructor_NamedCollectionExpression_InlinesItsElements()
    {
        var output = Emit(
            "public Card Make() => new Card(\"t\", onClose: () => { }, content: [P(), P()]);"
        );

        await Assert.That(output).Contains("}, Caller.p(), Caller.p())");
        await Assert.That(output).DoesNotContain("...[");
    }

    [Test]
    public async Task Constructor_NamedCollectionExpressionWithSpread_InlinesItsElementsAndKeepsTheSpread()
    {
        var output = Emit(
            "public Card Make(Part[] more) => new Card(\"t\", content: [P(), .. more]);"
        );

        await Assert.That(output).Contains("Caller.p(), ...more)");
        await Assert.That(output).DoesNotContain("...[");
    }

    [Test]
    public async Task Constructor_PositionalArrayVariable_IsSpread()
    {
        var output = Emit("public Card Make(Part[] arr) => new Card(\"t\", null, null, arr);");

        await Assert.That(output).Contains("...arr)");
    }

    [Test]
    public async Task InstanceMethod_NamedArrayArgument_IsSpread()
    {
        var output = Emit(
            "public int Run(Card card, Part[] arr) => card.Count(\"x\", items: arr);"
        );

        await Assert.That(output).Contains("...arr)");
    }

    [Test]
    public async Task InstanceMethod_PositionalArrayVariable_IsSpread()
    {
        var output = Emit("public int Run(Card card, Part[] arr) => card.Count(\"x\", 1, arr);");

        await Assert.That(output).Contains("...arr)");
    }

    [Test]
    public async Task StaticMethod_NamedArrayArgument_IsSpread()
    {
        var output = Emit("public int Run(Part[] arr) => Card.Total(\"x\", items: arr);");

        await Assert.That(output).Contains("...arr)");
    }

    [Test]
    public async Task ExtensionMethod_NamedArrayArgument_IsSpread()
    {
        var output = Emit("public int Run(Card card, Part[] arr) => card.Tally(items: arr);");

        await Assert.That(output).Contains("...arr)");
    }

    [Test]
    public async Task BaseConstructor_NamedArrayArgument_IsSpread()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result["app/fancy.ts"]).Contains("...parts)");
        await Assert.That(result["app/fancier.ts"]).Contains("...parts)");
    }

    [Test]
    public async Task Constructor_ReorderedNamedArguments_EvaluateTheArrayOnceInSourceOrder()
    {
        var output = Emit(
            """
            private static int _n;
            private static Part[] Parts() { _n++; return []; }
            private static string Title() { _n++; return "t"; }

            public Card Make() => new Card(content: Parts(), title: Title());
            """
        );

        await Assert.That(output).Contains("$arg3 = Caller.parts(), $arg0 = Caller.title()");
        await Assert.That(output).Contains("new Card($arg0, $arg1, $arg2, ...$arg3)");
    }

    [Test]
    public async Task ExpandedForm_StaysDiscrete()
    {
        var output = Emit(
            """
            public Card Make() => new Card("t", null, null, P(), P());

            public int Run(Card card) => card.Count("x", 1, P(), P());

            public Card Named() => new Card("t", content: P());
            """
        );

        await Assert.That(output).DoesNotContain("...");
    }

    [Test]
    public async Task Constructor_NamedNullArray_StaysDroppedAsInThePositionalForm()
    {
        // #756 owns the runtime-null case; a constant null keeps the #739 rule in the named form too.
        var output = Emit(
            "public Card Make() => new Card(\"t\", onClose: () => { }, content: null!);"
        );

        await Assert.That(output).DoesNotContain("...null");
        await Assert.That(output).DoesNotContain("null)");
    }

    [Test]
    public async Task EveryCallForm_TypeChecksStrictly()
    {
        var files = TranspileHelper.Transpile(
            Source
                + """

                [Transpile]
                public sealed class Caller
                {
                    private static Part P() => new Part();

                    public Card A(Part[] arr) => new Card("t", onClose: () => { }, content: arr);

                    public Card B() => new Card("t", content: [P(), P()]);

                    public Card C(Part[] arr) => new Card("t", null, null, arr);

                    public Card D() => new Card("t", null, null, P(), P());

                    public int E(Card card, Part[] arr) =>
                        card.Count("x", items: arr) + Card.Total("x", items: arr) + card.Tally(items: arr);

                    public Card F(Part[] arr) => new Card(content: arr, title: "t");

                    public Card G(Part[] more) => new Card("t", content: [P(), .. more]);

                    public Card H() => new Card("t", content: []);
                }
                """
        );

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ParamsCollection_NormalForm_PassesTheCollectionUnchanged()
    {
        // `params IEnumerable<T>` / `params List<T>` (C# 13) lower to ordinary parameters, not a rest
        // parameter, so the normal form passes the collection as it is. `params ReadOnlySpan<T>` is #730.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            namespace App;

            [Transpile]
            public sealed class Coll
            {
                public int A(string l, int pad = 0, params IEnumerable<int> xs) => xs.Count();
                public int C(string l, int pad = 0, params List<int> xs) => xs.Count;

                public int Run(int[] arr, List<int> list) =>
                    A("a", xs: arr) + A("a", 1, arr) + C("c", xs: list) + C("c", 1, list);
            }
            """
        );

        var output = result["app/coll.ts"];

        await Assert.That(output).Contains("this.a(\"a\", 0, arr) + this.a(\"a\", 1, arr)");
        await Assert.That(output).Contains("this.c(\"c\", 0, list) + this.c(\"c\", 1, list)");
    }

    [Test]
    public async Task Constructor_NamedNullArrayOutOfOrder_DropsTheArgumentAndReportsIt()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Source
                + """

                [Transpile]
                public sealed class Caller
                {
                    public Card Make() => new Card(content: null!, title: "t");
                }
                """
        );

        var output = result["app/caller.ts"];

        await Assert.That(output).Contains("new Card(\"t\", null, null)");
        await Assert.That(output).DoesNotContain("...null");
        await Assert.That(diagnostics.Any(d => d.Message.Contains("null array"))).IsTrue();
    }

    [Test]
    public async Task DynamicCall_PassesTheArgumentsAsWritten()
    {
        // The callee of a dynamic call is unknown at transpile time, so no form can be told apart.
        var output = Emit("public int Run(dynamic target, Part[] arr) => target.Foo(\"t\", arr);");

        await Assert.That(output).Contains("target.Foo(\"t\", arr)");
    }

    private const string PlainObjectSource = """
        namespace App;

        [Transpile, PlainObject]
        public record Dto(string Title, string? Sub = null, params string[] Tags);

        [Transpile]
        public sealed class Caller
        {
            public Dto Normal(string[] tags) => new Dto("t", null, tags);

            public Dto Named(string[] tags) => new Dto("t", Tags: tags);

            public Dto Literal() => new Dto("t", Tags: ["x", "y"]);

            public Dto Expanded() => new Dto("t", null, "a", "b");

            public Dto Omitted() => new Dto("t");
        }
        """;

    [Test]
    public async Task PlainObject_ParamsParameter_AssignsAnArrayInEveryForm()
    {
        var result = TranspileHelper.Transpile(PlainObjectSource);
        var output = result["app/caller.ts"];

        await Assert.That(output).Contains("tags: tags");
        await Assert.That(output).Contains("tags: [\"x\", \"y\"]");
        await Assert.That(output).Contains("tags: [\"a\", \"b\"]");
        await Assert.That(output).Contains("tags: []");
        await Assert.That(output).DoesNotContain("...tags");
        await Assert.That(output).DoesNotContain("_3");
    }

    [Test]
    public async Task PlainObject_ParamsParameter_TypeChecksStrictly()
    {
        var files = TranspileHelper.Transpile(PlainObjectSource);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ConditionalAccessCall_NamedArrayArgument_IsSpread()
    {
        var output = Emit(
            "public int? Run(Card? card, Part[] arr) => card?.Count(\"x\", items: arr);"
        );

        await Assert.That(output).Contains("...arr)");
    }

    [Test]
    public async Task PrimaryConstructorBase_NamedArrayArgument_IsSpread()
    {
        var result = TranspileHelper.Transpile(
            Source
                + """

                [Transpile]
                public class Primary(Part[] parts) : Card("t", content: parts);
                """
        );

        await Assert.That(result["app/primary.ts"]).Contains("...parts)");
    }

    [Test]
    public async Task EmitTemplate_NamedAndPositionalArrayArgument_LowerAlike()
    {
        // A template addresses its arguments by position, so it receives the array itself either way.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public static class Tpl
            {
                [Emit("$0.joined($1)")]
                public static string Join(string sep, params string[] xs) => "";
            }

            [Transpile]
            public sealed class Caller
            {
                public string Named(string[] a) => Tpl.Join(",", xs: a);

                public string Positional(string[] a) => Tpl.Join(",", a);
            }
            """
        );

        var output = result["app/caller.ts"];

        await Assert.That(output).Contains("return \",\".joined(a);");
        await Assert.That(output.Split("\",\".joined(a)").Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task BclMapping_NamedArrayArgument_KeepsTheMappedShape()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Caller
            {
                public string Join(string[] a) => string.Join(separator: ",", value: a);
            }
            """
        );

        await Assert.That(result["app/caller.ts"]).Contains("a.join(\",\")");
    }
}
