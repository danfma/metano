namespace Metano.Tests;

public class DelegateEventTests
{
    [Test]
    public async Task ActionType_MapsToArrowFunctionType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Notifier
            {
                public Action<string>? Callback { get; set; }
            }
            """
        );

        var output = result["notifier.ts"];
        // Action<string> → (obj: string) => void (Roslyn names the param "obj")
        await Assert.That(output).Contains("(obj: string) => void");
    }

    [Test]
    public async Task EventField_EmitsFieldAndAddRemoveMethods()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public event Action<int>? CountChanged;
            }
            """
        );

        var output = result["counter.ts"];
        // Field: nullable delegate
        await Assert.That(output).Contains("countChanged:");
        await Assert.That(output).Contains("| null = null");
        // $add method
        await Assert.That(output).Contains("countChanged$add(handler:");
        await Assert.That(output).Contains("delegateAdd(");
        // $remove method
        await Assert.That(output).Contains("countChanged$remove(handler:");
        await Assert.That(output).Contains("delegateRemove(");
    }

    [Test]
    public async Task EventField_ImportsRuntimeHelpers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public event Action<int>? CountChanged;
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("import { delegateAdd, delegateRemove }");
        await Assert.That(output).Contains("from \"metano-runtime\"");
    }

    [Test]
    public async Task EventSubscription_LowersToAddMethod()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Counter
            {
                public event Action<int>? CountChanged;
            }

            [Transpile]
            public class App
            {
                private Counter _counter = new Counter();

                public void Setup()
                {
                    _counter.CountChanged += OnCountChanged;
                }

                private void OnCountChanged(int count) { }
            }
            """
        );

        var output = result["app/app.ts"];
        await Assert.That(output).Contains("countChanged$add(");
    }

    [Test]
    public async Task EventUnsubscription_LowersToRemoveMethod()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Counter
            {
                public event Action<int>? CountChanged;
            }

            [Transpile]
            public class App
            {
                private Counter _counter = new Counter();

                public void Teardown()
                {
                    _counter.CountChanged -= OnCountChanged;
                }

                private void OnCountChanged(int count) { }
            }
            """
        );

        var output = result["app/app.ts"];
        await Assert.That(output).Contains("countChanged$remove(");
    }

    // ── Method group → delegate binding ──────────────────────────

    [Test]
    public async Task MethodGroupAssignment_InstanceMethod_BindsThis()
    {
        // C# method group conversion captures the receiver
        // implicitly. JS doesn't auto-bind, so the IR must wrap the
        // member access in `.bind(this)` before assigning to the
        // delegate field — otherwise invoking the delegate via
        // another object loses `this` and the body's member access
        // crashes at runtime.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class View
            {
                public Action? OnClick { get; set; }
            }

            [Transpile]
            public class Presenter
            {
                private readonly View _view = new();

                public void Setup()
                {
                    _view.OnClick = OnButtonClicked;
                }

                private void OnButtonClicked() { }
            }
            """
        );

        var output = result["app/presenter.ts"];
        await Assert.That(output).Contains("this._view.onClick = this.onButtonClicked.bind(this)");
        await Assert.That(output).DoesNotContain("bindReceiver");
    }

    [Test]
    public async Task MethodGroupAssignment_StaticMethod_SkipsBind()
    {
        // Static method groups have no instance to capture; the
        // emitted reference stays bare. Wrapping in `.bind(...)`
        // would be wasted allocation.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class View
            {
                public Action? OnClick { get; set; }
            }

            [Transpile]
            public static class Handlers
            {
                public static void HandleClick() { }
            }

            [Transpile]
            public class Presenter
            {
                public void Setup(View view)
                {
                    view.OnClick = Handlers.HandleClick;
                }
            }
            """
        );

        var output = result["app/presenter.ts"];
        await Assert.That(output).Contains("view.onClick = Handlers.handleClick");
        await Assert.That(output).DoesNotContain("Handlers.handleClick.bind");
    }

    [Test]
    public async Task MethodGroupAssignment_ImpureReceiver_EvaluatesChainOnce()
    {
        // The receiver chain has observable side effects (a method
        // call). Duplicating it in `chain.method.bind(chain)` would
        // run the call twice. Wrap the bind site in an IIFE arrow
        // that captures the receiver in a temporary so the chain is
        // evaluated exactly once.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Service
            {
                public Action OnTick { get; set; } = () => { };
                public void Tick() { }
            }

            [Transpile]
            public class Cache
            {
                public Service Service { get; } = new();
            }

            [Transpile]
            public class Worker
            {
                private Cache GetCache() => new();

                public void Wire(Service target)
                {
                    target.OnTick = GetCache().Service.Tick;
                }
            }
            """
        );

        var output = result["app/worker.ts"];
        // The IIFE captures the receiver once; the bind argument
        // reads from the captured temporary instead of re-running
        // the chain. The receiver expression itself appears exactly
        // once in the emitted text.
        await Assert.That(output).Contains("__r.tick.bind(__r)");
        await Assert.That(output).Contains(")(this.getCache().service)");
        var occurrences = System
            .Text.RegularExpressions.Regex.Matches(output, @"this\.getCache\(\)")
            .Count;
        await Assert.That(occurrences).IsEqualTo(1);
    }

    [Test]
    public async Task MethodGroupAsArgument_InstanceMethod_BindsThis()
    {
        // Method group passed as a delegate-typed argument flows
        // through the same path — bind preserves `this` inside the
        // callee.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Scheduler
            {
                public void Run(Action callback) { }
            }

            [Transpile]
            public class Worker
            {
                private readonly Scheduler _scheduler = new();

                public void Start()
                {
                    _scheduler.Run(Tick);
                }

                private void Tick() { }
            }
            """
        );

        var output = result["app/worker.ts"];
        await Assert.That(output).Contains("this._scheduler.run(this.tick.bind(this))");
        await Assert.That(output).DoesNotContain("bindReceiver");
    }

    [Test]
    public async Task MethodGroupReturn_InstanceMethod_BindsThis()
    {
        // Returning a method group as a delegate must bind too —
        // the caller will invoke the result through whatever
        // dispatcher it owns and lose `this` otherwise.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Box
            {
                private int _value;

                public Func<int> GetReader()
                {
                    return Read;
                }

                private int Read() => _value;
            }
            """
        );

        var output = result["app/box.ts"];
        await Assert.That(output).Contains("return this.read.bind(this)");
        await Assert.That(output).DoesNotContain("bindReceiver");
    }

    [Test]
    public async Task EventSubscription_InstanceHandler_BindsThis()
    {
        // `event += handler` lowers to `event$add(handler)`. The
        // method group inside the assignment still flows through
        // the wrapper, so the handler arrives at `event$add`
        // already bound. No double-binding because the bind happens
        // at the leaf reference.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Counter
            {
                public event Action<int>? CountChanged;
            }

            [Transpile]
            public class Watcher
            {
                private readonly Counter _counter = new();

                public void Listen()
                {
                    _counter.CountChanged += OnChanged;
                }

                private void OnChanged(int n) { }
            }
            """
        );

        var output = result["app/watcher.ts"];
        await Assert.That(output).Contains("countChanged$add(this.onChanged.bind(this))");
        await Assert.That(output).DoesNotContain("bind(this).bind");
    }

    [Test]
    public async Task OrdinaryMethodCall_DoesNotBind()
    {
        // Plain method invocation must stay un-bound. The wrapper
        // relies on Roslyn reporting no `ConvertedType` for method
        // groups used as call targets — pin that invariant so a
        // future Roslyn change does not silently double-bind every
        // call site.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Calculator
            {
                public void Run()
                {
                    Tick();
                }

                private void Tick() { }
            }
            """
        );

        var output = result["app/calculator.ts"];
        await Assert.That(output).Contains("this.tick()");
        await Assert.That(output).DoesNotContain(".bind(this)");
    }

    [Test]
    public async Task BaseMethodGroup_BindsThisInsteadOfSuper()
    {
        // `base.Method` lowers to `super.method` in TS, but `super`
        // is not a value expression — it cannot be passed to
        // `.bind(...)`. The wrapper substitutes `this` in the bind
        // argument so the emitted JS stays valid; the base method
        // body still runs against the current instance, matching
        // the C# semantics of `base`-qualified method groups
        // (non-virtual dispatch but receiver stays `this`).
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class BaseHandler
            {
                public virtual void Handle() { }
            }

            [Transpile]
            public class DerivedHandler : BaseHandler
            {
                public Action GetBaseHandler()
                {
                    return base.Handle;
                }
            }
            """
        );

        var output = result["app/derived-handler.ts"];
        await Assert.That(output).Contains("super.handle.bind(this)");
        await Assert.That(output).DoesNotContain("super.handle.bind(super)");
    }

    [Test]
    public async Task BaseMemberAccessChain_MethodGroup_RecursivelySubstitutesSuper()
    {
        // `base.Forwarder.Handle` ends a member-access chain rooted
        // in `base`. The bind argument must walk the chain and
        // substitute `this` for the `super` root — `super.forwarder`
        // is fine on the LHS (super property access is valid JS),
        // but the bind argument cannot keep `super` because it is
        // not a value expression.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Inner
            {
                public void Handle() { }
            }

            [Transpile]
            public class BaseHandler
            {
                protected Inner Forwarder { get; } = new();
            }

            [Transpile]
            public class DerivedHandler : BaseHandler
            {
                public Action GetBaseHandler() => base.Forwarder.Handle;
            }
            """
        );

        var output = result["app/derived-handler.ts"];
        await Assert.That(output).Contains(".bind(this.forwarder)");
        await Assert.That(output).DoesNotContain(".bind(super.forwarder)");
    }

    [Test]
    public async Task ThisDelegate_InstanceMethodGroup_StacksBindAndBindReceiver()
    {
        // `[This]`-bearing delegate assigned an instance method
        // group must stack both wraps: `.bind(this)` on the inner
        // reference (so the body's own `this` survives the
        // dispatcher rebind) plus `bindReceiver(...)` on the
        // outside (so the dispatcher rewrites JS `this` into the
        // first parameter at call time).
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App;

            [Transpile]
            public abstract class Element
            {
                public string Tag { get; set; } = "";
            }

            public delegate void Listener([This] Element self);

            [Transpile]
            public class Widget
            {
                public Listener? OnClick { get; set; }
            }

            [Transpile]
            public class Page
            {
                private readonly Widget _widget = new();

                public void Setup()
                {
                    _widget.OnClick = OnButtonClicked;
                }

                private void OnButtonClicked(Element self) { }
            }
            """
        );

        var output = result["app/page.ts"];
        await Assert.That(output).Contains("bindReceiver(this.onButtonClicked.bind(this))");
    }

    [Test]
    public async Task FunctionTypedParam_ImportsTypesFromSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IWidget { }

            [Transpile]
            public static class App
            {
                public static void Mount(Func<IWidget> view) { }
            }
            """
        );

        await AssertImportsName(result["app.ts"], "IWidget", "./i-widget");
        await Assert.That(result["app.ts"]).Contains("() => IWidget");
    }

    [Test]
    public async Task FunctionTypedParam_ImportsTypesFromNestedFunctionTypes()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IWidget { }

            [Transpile]
            public static class App
            {
                public static void Mount(Action<Func<IWidget>> register) { }
            }
            """
        );

        await AssertImportsName(result["app.ts"], "IWidget", "./i-widget");
    }

    [Test]
    public async Task ValueTupleTypedProperty_ImportsTypesFromTupleElements()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IWidget { }

            [Transpile]
            public class Slot
            {
                public (string Key, IWidget Value) Entry { get; set; }
            }
            """
        );

        var output = result["slot.ts"];
        await AssertImportsName(output, "IWidget", "./i-widget");
        await Assert.That(output).Contains("[string, IWidget]");
    }

    private static async Task AssertImportsName(string output, string name, string path)
    {
        var importLines = output
            .Split('\n')
            .Where(line => line.Contains($"{{ {name} }}") && line.Contains($"\"{path}\""))
            .ToList();
        await Assert.That(importLines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DartTarget_DoesNotEmitBind()
    {
        // Dart tear-offs auto-bind, so the JS-only `.bind(this)`
        // idiom must not appear in Dart output. The wrapper opts
        // out via the `IsDartTarget` guard.
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class View
            {
                public Action? OnClick { get; set; }
            }

            [Transpile]
            public class Presenter
            {
                private readonly View _view = new();

                public void Setup()
                {
                    _view.OnClick = OnButtonClicked;
                }

                private void OnButtonClicked() { }
            }
            """
        );

        var dart = files["presenter.dart"];
        await Assert.That(dart).Contains("onButtonClicked");
        await Assert.That(dart).DoesNotContain(".bind(");
        await Assert.That(dart).DoesNotContain("bindReceiver");
    }

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileDart(string csharpSource)
    {
        var source = $"""
            using System;
            using Metano.Annotations;
            {csharpSource}
            """;
        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            source,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview
            )
        );
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "DartTestAssembly",
            [syntaxTree],
            TranspileHelper.BaseReferences,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary
            )
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "C# compilation failed:\n" + string.Join("\n", errors.Select(e => e.ToString()))
            );

        var ir = new Metano.Compiler.CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            Metano.Annotations.TargetLanguage.Dart
        );
        var transformer = new Metano.Compiler.Dart.Transformation.DartTransformer(ir, compilation);
        var files = transformer.TransformAll();
        var printer = new Metano.Compiler.Dart.Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }
}
