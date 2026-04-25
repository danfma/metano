using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for Slice A of <c>[This]</c> (issue #113): the attribute
/// promotes the first parameter of a delegate to the synthetic
/// JavaScript <c>this</c> receiver. In this slice the TS delegate
/// type emits with a <c>(this: T, …)</c> signature and the validator
/// rejects misuse (non-first position, <c>ref</c>/<c>out</c>/<c>params</c>).
/// Body rewriting, lambda <c>function</c>-keyword emission, and
/// method-group assignment land in later slices.
/// </summary>
public class ThisAttributeTranspileTests
{
    [Test]
    public async Task This_OnDelegateFirstParameter_EmitsThisAnnotationInFunctionType()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void MouseEventListener([This] Element self, string arg);

            public class Widget
            {
                public MouseEventListener? OnClick { get; set; }
            }
            """
        );

        // The delegate-typed property on `Widget` lowers to the
        // TypeScript function type; the emitted signature carries the
        // synthetic `this: Element` slot and the remaining
        // parameters after the one dropped by the attribute.
        var output = result["widget.ts"];
        await Assert.That(output).Contains("(this: Element, arg: string) => void");
    }

    [Test]
    public async Task This_OnNonFirstParameter_EmitsMs0018()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element {}

            public delegate void BadListener(Element self, [This] string arg);
            """
        );

        var ms0018 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidThis);
        await Assert.That(ms0018).IsNotNull();
        await Assert.That(ms0018!.Message).Contains("first positional parameter");
    }

    [Test]
    public async Task This_OnRefParameter_EmitsMs0018()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element {}

            public delegate void RefListener([This] ref Element self, string arg);
            """
        );

        var ms0018 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidThis);
        await Assert.That(ms0018).IsNotNull();
        await Assert.That(ms0018!.Message).Contains("'ref'");
    }

    [Test]
    public async Task This_OnInParameter_EmitsMs0018()
    {
        // `in` parameters pass by readonly-reference at the CLR
        // level; they cannot act as the JS `this` receiver at the
        // boundary. Guard alongside `ref` / `out` / `ref readonly`.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element {}

            public delegate void InListener([This] in Element self, string arg);
            """
        );

        var ms0018 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidThis);
        await Assert.That(ms0018).IsNotNull();
        await Assert.That(ms0018!.Message).Contains("'in'");
    }

    [Test]
    public async Task This_OnParamsParameter_EmitsMs0018()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public delegate void VariadicListener([This] params string[] values);
            """
        );

        var ms0018 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidThis);
        await Assert.That(ms0018).IsNotNull();
        await Assert.That(ms0018!.Message).Contains("'params'");
    }

    // ─── Slice B: lambda binding + body rewrite ──────────────

    [Test]
    public async Task This_LambdaAssignedToDelegate_WrappedInBindReceiver()
    {
        // A lambda bound to a `[This]`-bearing delegate lowers to a
        // plain arrow wrapped in the runtime `bindReceiver` helper.
        // The helper's `function`-keyword trampoline captures the
        // JS dispatcher's `this` and forwards it as the first
        // positional argument to the arrow, so the user's original
        // `self` parameter carries the receiver without the arrow
        // itself having to surrender its lexical `this`.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void MouseEventListener([This] Element self, string arg);

            public class Widget
            {
                public MouseEventListener? OnClick { get; set; }

                public void Register()
                {
                    OnClick = (self, arg) => self.InnerHtml = arg;
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("bindReceiver((self: Element, arg: string) =>");
        await Assert.That(output).Contains("self.innerHtml = arg");
        // The import line pulls `bindReceiver` from the runtime
        // package so the emitted file is compilable.
        await Assert.That(output).Contains("import { bindReceiver }");
        await Assert.That(output).Contains("\"metano-runtime\"");
    }

    [Test]
    public async Task This_LambdaWithoutTargetDelegate_StillEmitsPlainArrow()
    {
        // Regression guard: lambdas assigned to ordinary delegates
        // (no `[This]`) stay on the plain arrow path with no
        // bindReceiver wrapper.
        var result = TranspileHelper.Transpile(
            """
            using System;
            [assembly: TranspileAssembly]

            public class Widget
            {
                public Action<string>? Handler { get; set; }

                public void Register()
                {
                    Handler = (arg) => System.Console.WriteLine(arg);
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("(arg: string) =>");
        await Assert.That(output).DoesNotContain("bindReceiver");
    }

    [Test]
    public async Task This_SingleParameterLambda_WrappedInBindReceiver()
    {
        // `self => self.InnerHtml = "ready"` — the only parameter is
        // the receiver. The emitted shape is still a plain arrow
        // wrapped in `bindReceiver`; the arrow retains the receiver
        // parameter so the helper can forward the runtime `this`.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self);

            public class Widget
            {
                public Listener? OnLoad { get; set; }

                public void Register()
                {
                    OnLoad = self => self.InnerHtml = "ready";
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("bindReceiver((self: Element) =>");
        await Assert.That(output).Contains("self.innerHtml");
    }

    [Test]
    public async Task This_NestedLambdas_EachReceiverFlowsThroughItsOwnBindReceiver()
    {
        // Nested `[This]` lambdas each wrap themselves in
        // `bindReceiver`; the arrows stay lexically scoped so an
        // outer receiver referenced from inside the nested arrow is
        // a plain closure capture. Nothing in the body rewrites to
        // the keyword `this` — the receiver flows through the
        // user-chosen parameter name on each level.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
                public Listener? OnClick { get; set; }
            }

            public delegate void Listener([This] Element self);

            public class Widget
            {
                public Listener? OnLoad { get; set; }

                public void Register()
                {
                    OnLoad = outer =>
                    {
                        outer.OnClick = inner => inner.InnerHtml = outer.InnerHtml;
                    };
                }
            }
            """
        );

        var output = result["widget.ts"];
        // Outer + inner each wrapped in their own bindReceiver call.
        var outerWrap = output.IndexOf(
            "bindReceiver((outer: Element) =>",
            System.StringComparison.Ordinal
        );
        var innerWrap = output.IndexOf(
            "bindReceiver((inner: Element) =>",
            System.StringComparison.Ordinal
        );
        await Assert.That(outerWrap).IsGreaterThanOrEqualTo(0);
        await Assert.That(innerWrap).IsGreaterThanOrEqualTo(0);
        // Inner body references both receivers by their original
        // names — the outer one is a closure capture, not `this`.
        await Assert.That(output).Contains("inner.innerHtml = outer.innerHtml");
    }

    [Test]
    public async Task This_AsyncLambda_WrappedInBindReceiver()
    {
        // Regression guard: an `async` lambda bound to a `[This]`
        // delegate still lowers through `bindReceiver`; the helper
        // accepts an async arrow unchanged.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using System.Threading.Tasks;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate Task AsyncListener([This] Element self);

            public class Widget
            {
                public AsyncListener? OnLoad { get; set; }

                public void Register()
                {
                    OnLoad = async self => { self.InnerHtml = "ready"; };
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("bindReceiver(async (self: Element) =>");
        await Assert.That(output).Contains("self.innerHtml");
    }

    [Test]
    public async Task This_MethodGroupAssignment_WrapsReferenceInBindReceiver()
    {
        // Slice C: assigning a method group to a `[This]`-bearing
        // delegate must wrap the reference in `bindReceiver`. The
        // extractor detects the method-group → delegate conversion
        // (identical mechanism to lambda target-type detection) and
        // funnels the runtime `this` into the named handler's first
        // parameter at every invocation.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self, string arg);

            public class Widget
            {
                public Listener? OnClick { get; set; }

                private void Handler(Element self, string arg)
                {
                    self.InnerHtml = arg;
                }

                public void Register()
                {
                    OnClick = Handler;
                }
            }
            """
        );

        var output = result["widget.ts"];
        // Instance method group must `.bind(this)` before the
        // bindReceiver wrap so the body's own `this` survives the
        // dispatcher's runtime rebinding. Otherwise the C# method
        // body would see `this === undefined` and any field access
        // (`this.someState`) would crash at runtime.
        await Assert.That(output).Contains("bindReceiver(this.handler.bind(this))");
        await Assert.That(output).Contains("import { bindReceiver }");
    }

    [Test]
    public async Task This_StaticMethodGroupAssignment_WrapsReferenceInBindReceiver()
    {
        // Static method group: the reference goes through the
        // class-qualified path. Same wrap applies — the static
        // method call would otherwise lose the dispatcher's `this`.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self);

            public class Handlers
            {
                public static void OnLoad(Element self)
                {
                    self.InnerHtml = "ready";
                }
            }

            public class Widget
            {
                public Listener? OnLoad { get; set; }

                public void Register()
                {
                    OnLoad = Handlers.OnLoad;
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("bindReceiver(Handlers.onLoad)");
    }

    [Test]
    public async Task This_MethodGroupForPlainDelegate_NotWrapped()
    {
        // Regression guard: method groups assigned to plain (non-
        // `[This]`) delegates stay on the bare-reference path.
        var result = TranspileHelper.Transpile(
            """
            using System;
            [assembly: TranspileAssembly]

            public class Widget
            {
                public Action<string>? Handler { get; set; }

                private void Run(string s) {}

                public void Register()
                {
                    Handler = Run;
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("this.handler = this.run");
        await Assert.That(output).DoesNotContain("bindReceiver");
    }

    [Test]
    public async Task This_DirectInvocation_LowersThroughDotCall()
    {
        // Slice C: invoking a `[This]`-bearing delegate directly from
        // C# routes through `.call(receiver, ...rest)` so the JS
        // dispatcher sets `this` to the first argument before the
        // bindReceiver trampoline forwards it. Without the
        // `.call(...)` shape, the delegate fires with `this ===
        // undefined` at the wrapper boundary.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self, string arg);

            public class Widget
            {
                public void Trigger(Listener handler, Element target)
                {
                    handler(target, "fired");
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("handler.call(target, \"fired\")");
        await Assert.That(output).DoesNotContain("handler(target, \"fired\")");
    }

    [Test]
    public async Task This_CrossAssembly_DelegateReadFromReferencedLibrary()
    {
        // Slice D: a `[This]`-bearing delegate declared in a
        // referenced library must propagate the attribute to consumer
        // emission. The library project ships the delegate; the
        // consumer assigns a lambda to a property typed by it.
        // SymbolHelper.HasThis already does namespace-qualified
        // matching against `Metano.Annotations`, so reading the
        // attribute off a CompilationReference symbol works
        // automatically — this test pins the behavior end-to-end so
        // a future refactor cannot quietly drop it.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self, string arg);

            public class Widget
            {
                public Listener? OnClick { get; set; }
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Consumer
            {
                public void Wire(Widget w)
                {
                    w.OnClick = (self, arg) => self.InnerHtml = arg;
                }
            }
            """
        );

        var output = result["consumer.ts"];
        await Assert.That(output).Contains("bindReceiver((self: Element, arg: string)");
        await Assert.That(output).Contains("self.innerHtml = arg");
        await Assert.That(output).Contains("import { bindReceiver }");
    }

    [Test]
    public async Task This_GenericMethodGroupAssignment_WrapsReferenceInBindReceiver()
    {
        // Generic method-group references go through GenericNameSyntax
        // extraction; that path must reuse the same wrap helper as
        // ordinary identifier extraction so `Handler<int>` lands in
        // `bindReceiver(...)` like `Handler` does.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self, string arg);

            public class Widget
            {
                public Listener? OnClick { get; set; }

                private void Handler<T>(Element self, string arg)
                {
                    self.InnerHtml = arg;
                }

                public void Register()
                {
                    OnClick = Handler<int>;
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("bindReceiver(this.handler.bind(this))");
    }

    [Test]
    public async Task This_DirectInvocation_NamedReceiverArgument_LowersThroughDotCall()
    {
        // Named-argument invocation must still produce the correct
        // `.call(...)` shape: the receiver is identified by parameter
        // name, not by syntactic position.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self, string arg);

            public class Widget
            {
                public void Trigger(Listener handler, Element target)
                {
                    handler(arg: "fired", self: target);
                }
            }
            """
        );

        var output = result["widget.ts"];
        // Receiver picked by name → first arg of `.call`.
        await Assert.That(output).Contains("handler.call(target, \"fired\")");
    }

    [Test]
    public async Task This_LambdaCapturingOuterClassThis_KeepsLexicalBinding()
    {
        // The core motivation for `bindReceiver` vs. a
        // `function`-keyword rewrite: a `[This]` lambda body may
        // reference the enclosing C# class's `this` via closure.
        // The arrow's lexical `this` captures the enclosing class
        // instance naturally — the runtime `this` arrives as the
        // `self` parameter via the wrapper. No conflict.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            public delegate void Listener([This] Element self);

            public class Widget
            {
                public string Name { get; set; } = "widget";
                public Listener? OnClick { get; set; }

                public void Register()
                {
                    OnClick = self => self.InnerHtml = this.Name;
                }
            }
            """
        );

        var output = result["widget.ts"];
        // The arrow body reads the outer class's `this.name` via
        // lexical closure — untouched by the wrapper.
        await Assert.That(output).Contains("self.innerHtml = this.name");
    }

    [Test]
    public async Task This_OnSingleParameterDelegate_EmitsEmptyParameterList()
    {
        // `[This]` on the only parameter leaves the delegate with no
        // positional arguments; the emitted signature reads
        // `(this: T) => R` with no trailing comma.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public abstract class Element {}

            public delegate void Listener([This] Element self);

            public class Host
            {
                public Listener? Handler { get; set; }
            }
            """
        );

        var output = result["host.ts"];
        await Assert.That(output).Contains("(this: Element) => void");
        await Assert.That(output).DoesNotContain("(this: Element, ");
    }
}
