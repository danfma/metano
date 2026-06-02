namespace Metano.Tests;

/// <summary>
/// Golden tests for the JSX/TSX lowering (feature 002): component records lower
/// to TSX function components (US1) and JSX-typed object creations lower to JSX
/// elements (US2). Each test compiles inline C# against the real SolidJS + DOM
/// bindings and pins the emitted <c>.tsx</c> against the contract fixtures in
/// <c>Expected/</c>.
/// </summary>
public class SolidJsJsxTranspileTests
{
    // Shared inline using-block so every case sees the JSX binding surface.
    private const string Usings = """
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;
        using Metano.TypeScript.SolidJs;
        using Metano.TypeScript.SolidJs.Web;
        using Metano.TypeScript.DOM;
        """;

    // ── US1: component record → TSX function component ──────────────────────

    [Test]
    public async Task C1_EmptyComponent_NoProps()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record Hello : JsxComponent
            {
                public override JsxElement Render() => new Html.Div { Children = [Text("hi")] };
            }
            """
        );

        await Assert
            .That(result["hello.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-hello.tsx"));
    }

    [Test]
    public async Task C2_PropWithDefaultAndHoist()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record Counter : JsxComponent
            {
                public int Count { get; init; }
                public override JsxElement Render() => new Html.Span { Children = [Text(Count)] };
            }
            """
        );

        await Assert
            .That(result["counter.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-counter-span.tsx"));
    }

    [Test]
    public async Task C3_MembershipRules()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record Membership : JsxComponent
            {
                public int A { get; init; }
                public string B { get; set; }
                public int C => A + 1;
                [Ignore]
                public int D { get; init; }
                public override JsxElement Render() => new Html.Div { Children = [Text("x")] };
            }
            """
        );

        await Assert
            .That(result["membership.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-membership.tsx"));
    }

    [Test]
    public async Task C4_NameOverride()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile, Name("AppRoot")]
            public sealed record Root : JsxComponent
            {
                public string Title { get; init; }
                public override JsxElement Render() => new Html.Div { Children = [Text("x")] };
            }
            """
        );

        await Assert
            .That(result["app-root.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-named-component.tsx"));
    }

    // ── US2: native element lowering ────────────────────────────────────────

    [Test]
    public async Task N1_AttributeNameMappingAndCamelCase()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record DivAttrs : JsxComponent
            {
                public override JsxElement Render() => new Html.Div { ClassName = "counter", Id = "c1" };
            }
            """
        );

        await Assert
            .That(result["div-attrs.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-div-attrs.tsx"));
    }

    [Test]
    public async Task N2_LiteralVsExpressionAttributeAndHandler()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record ButtonHandler : JsxComponent
            {
                public override JsxElement Render()
                {
                    MouseClickHandler<Html.Button> decrement = _ => { };
                    return new Html.Button { ClassName = "action", OnClick = decrement };
                }
            }
            """
        );

        await Assert
            .That(result["button-handler.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-button-handler.tsx"));
    }

    [Test]
    public async Task N3_ChildrenTextAndNestedElements()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record DivChildren : JsxComponent
            {
                public string Label { get; init; }
                public override JsxElement Render() =>
                    new Html.Div
                    {
                        Children =
                        [
                            new Html.Button { Children = [Text("-")] },
                            new Html.Span { Children = [Text(Label)] },
                        ],
                    };
            }
            """
        );

        await Assert
            .That(result["div-children.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-div-children.tsx"));
    }

    // ── US3: component composition ──────────────────────────────────────────

    [Test]
    public async Task N4_ComponentComposition()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record Counter : JsxComponent
            {
                public int Count { get; init; }
                public override JsxElement Render() => new Html.Span { Children = [Text(Count)] };
            }

            [Transpile]
            public sealed record CounterGroup : JsxComponent
            {
                public override JsxElement Render() =>
                    new Html.Div
                    {
                        Children = [new Counter { Count = 3 }, new Counter()],
                    };
            }
            """
        );

        await Assert
            .That(result["counter-group.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-counter-group.tsx"));
    }

    [Test]
    public async Task N5_RenderEntryLambda()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record CounterGroup : JsxComponent
            {
                public override JsxElement Render() => new Html.Div { Children = [Text("x")] };
            }

            [Transpile, ExportedAsModule]
            public static class App
            {
                [ModuleEntryPoint]
                public static void Main()
                {
                    var container = Js.Document.GetElementById("app")!;
                    SolidRenderer.Render(() => new CounterGroup(), container);
                }
            }
            """
        );

        await Assert
            .That(result["app.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-render-entry.tsx"));
    }

    // ── US4: SolidJS reactivity (signal facade elision) ─────────────────────

    [Test]
    public async Task R1ToR4_SignalCreateReadWriteEffect()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record Counter : JsxComponent
            {
                public int Count { get; init; }

                public override JsxElement Render()
                {
                    var (count, setCount) = Solid.CreateSignal(Count);
                    Solid.CreateEffect(() => Console.WriteLine(count()));
                    MouseClickHandler<Html.Button> decrement = _ => setCount.Invoke(count() - 1);
                    MouseClickHandler<Html.Button> increment = _ => setCount.Invoke(x => x + 1);
                    return new Html.Div
                    {
                        Children =
                        [
                            new Html.Button { ClassName = "dec", OnClick = decrement, Children = [Text("-")] },
                            new Html.Span { Children = [Text(count())] },
                            new Html.Button { ClassName = "inc", OnClick = increment, Children = [Text("+")] },
                        ],
                    };
                }
            }
            """
        );

        var tsx = result["counter.tsx"];

        // SC-002: no wrapper object survives anywhere in the output.
        await Assert.That(tsx).DoesNotContain("SignalWrapper");
        await Assert.That(tsx).DoesNotContain("metano-solid-js");
        await Assert.That(tsx).IsEqualTo(TranspileHelper.ReadExpected("jsx-signal-counter.tsx"));
    }

    [Test]
    public async Task R5_ForHelper()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}
            using System.Collections.Generic;

            [Transpile]
            public sealed record Counter : JsxComponent
            {
                public int Count { get; init; }
                public override JsxElement Render() => new Html.Span { Children = [Text(Count)] };
            }

            [Transpile]
            public sealed record CounterList : JsxComponent
            {
                public IReadOnlyList<int> Items { get; init; }
                public override JsxElement Render() =>
                    new Html.Div
                    {
                        Children =
                        [
                            Solid.For(Items, (item, index) => new Counter { Count = item }),
                        ],
                    };
            }
            """
        );

        await Assert
            .That(result["counter-list.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-for-helper.tsx"));
    }

    // ── US5: imported renderable + JSX-position diagnostic ──────────────────

    [Test]
    public async Task R7_ImportedRenderable_RoutEmitsTagAndModuleImport()
    {
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}
            using Metano.TypeScript.SolidJs.Routing;

            [Transpile]
            public sealed record Counter : JsxComponent
            {
                public int Count { get; init; }
                public override JsxElement Render() => new Html.Span { Children = [Text(Count)] };
            }

            [Transpile]
            public sealed record AppRouter : JsxComponent
            {
                public override JsxElement Render() =>
                    new Route
                    {
                        Path = "/",
                        Children = [new Counter { Count = 1 }],
                    };
            }
            """
        );

        await Assert
            .That(result["app-router.tsx"])
            .IsEqualTo(TranspileHelper.ReadExpected("jsx-imported-route.tsx"));
    }

    [Test]
    public async Task ChildrenSpread_IsNotTruncated()
    {
        // `Children = [..common, new Html.Span()]` must NOT silently drop the
        // spread (the old ExpressionElementSyntax-only extraction did). The spread
        // source lowers to a JSX expression child rendering the array (#7).
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            [Transpile]
            public sealed record Panel : JsxComponent
            {
                public JsxElement[] Common { get; init; } = [];
                public override JsxElement Render() =>
                    new Html.Div
                    {
                        Children = [..Common, new Html.Span()],
                    };
            }
            """
        );

        var tsx = result["panel.tsx"];
        // The spread source is rendered as an expression child (the array embeds
        // directly as a JSX child; Solid/JSX renders an array of elements), and
        // the trailing element survives — proving the spread was not truncated.
        await Assert.That(tsx).Contains("{props$common}");
        await Assert.That(tsx).Contains("<span />");
    }

    [Test]
    public async Task ForHelper_AsBareRenderReturn_LowersToForElement()
    {
        // `Render() => Solid.For(...)` is a call, not a `new`, and never passes
        // through the Children/LowerChild path. It must still lower to a <For>
        // element via the single JSX-producer entry point consulted by the
        // expression bridge (#2), not emit a raw `For(...)` call.
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}
            using System.Collections.Generic;

            [Transpile]
            public sealed record Counter : JsxComponent
            {
                public int Count { get; init; }
                public override JsxElement Render() => new Html.Span { Children = [Text(Count)] };
            }

            [Transpile]
            public sealed record CounterList : JsxComponent
            {
                public IReadOnlyList<int> Items { get; init; }
                public override JsxElement Render() =>
                    Solid.For(Items, (item, index) => new Counter { Count = item });
            }
            """
        );

        var tsx = result["counter-list.tsx"];
        await Assert.That(tsx).Contains("<For each={props$items}>");
        await Assert.That(tsx).Contains("{(item, index) => <Counter count={item} />}");
        await Assert.That(tsx).DoesNotContain("For(props$items");
    }

    [Test]
    public async Task TextBuilderCollision_UserStaticText_LowersToCallNotTextNode()
    {
        // A user-defined static `Util.Text(string)` returning JsxElement sits in
        // a Children slot. It is NOT the [JsxComponentBuilder] base's Text builder
        // (different declaring type), so it must lower to an expression child
        // (`{Util.text("x")}`) — never to a JSX text node. Pins the
        // declaring-type guard on IsTextBuilderCall (#3).
        var result = TranspileHelper.TranspileJsx(
            $$"""
            {{Usings}}

            public static class Util
            {
                public static JsxElement Text(string content) => null!;
            }

            [Transpile]
            public sealed record Widget : JsxComponent
            {
                public override JsxElement Render() =>
                    new Html.Div
                    {
                        Children = [Util.Text("x")],
                    };
            }
            """
        );

        var tsx = result["widget.tsx"];
        // Lowered as an expression child via a real call, NOT a bare text node.
        await Assert.That(tsx).Contains("{Util.text(\"x\")}");
        await Assert.That(tsx).DoesNotContain("<div>x</div>");
    }

    [Test]
    public async Task MS0026_NonRenderablePocoInChildren_RaisesDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileJsxWithDiagnostics(
            $$"""
            {{Usings}}

            // A POCO assignable to the Children slot (derives from the marked
            // JsxElement) but carrying no JSX marker — not a component, not a
            // native element, not an imported renderable.
            [Transpile]
            public sealed record PlainPoco : JsxElement;

            [Transpile]
            public sealed record BadComponent : JsxComponent
            {
                public override JsxElement Render() =>
                    new Html.Div
                    {
                        Children = [new PlainPoco()],
                    };
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0026")).IsTrue();
    }
}
