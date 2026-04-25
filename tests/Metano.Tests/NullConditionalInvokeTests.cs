using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Dart.Transformation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Lowering of <c>handler?.Invoke(...)</c> to TypeScript optional-call
/// <c>handler?.(...)</c> and Dart <c>handler?.call(...)</c>. Non-conditional
/// <c>handler.Invoke(...)</c> drops the <c>.Invoke</c> indirection on both
/// targets — the runtime delegate is itself callable.
/// </summary>
public class NullConditionalInvokeTests
{
    [Test]
    public async Task NullConditionalInvoke_NoArgs_LowersToOptionalCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Action? OnClick;

                public void Fire()
                {
                    OnClick?.Invoke();
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("this.onClick?.()");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task NullConditionalInvoke_WithArgs_LowersToOptionalCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Action<string, int>? OnEvent;

                public void Fire()
                {
                    OnEvent?.Invoke("hello", 42);
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("this.onEvent?.(\"hello\", 42)");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task DirectInvoke_DropsInvokeIndirection()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Action<string> Logger { get; }

                public Widget(Action<string> logger)
                {
                    Logger = logger;
                }

                public void Log()
                {
                    Logger.Invoke("msg");
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("this.logger(\"msg\")");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task NullConditionalInvoke_StaticDelegateField_NoImplicitThis()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Bus
            {
                public static Action<string>? Listener;

                public static void Publish(string msg)
                {
                    Listener?.Invoke(msg);
                }
            }
            """
        );

        var output = result["bus.ts"];
        await Assert.That(output).Contains("Bus.listener?.(msg)");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task NullConditionalInvoke_InsideLambda_PreservesOptionalCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Action? OnClick;

                public Action Wrap()
                {
                    return () => OnClick?.Invoke();
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("this.onClick?.()");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task DirectInvoke_GenericDelegate_DropsInvokeIndirection()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Func<int, string> Format { get; }

                public Widget(Func<int, string> format)
                {
                    Format = format;
                }

                public string Render(int value)
                {
                    return Format.Invoke(value);
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("this.format(value)");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task ThisBearingDelegate_DirectInvoke_KeepsCallRewrite()
    {
        // `[This]`-bearing delegates rebind the first argument as JS
        // `this`. The Invoke shortcut must fall through so the
        // existing `.call(receiver, ...)` rewrite stays in charge.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Element {}

            public delegate void MouseEventListener([This] Element self, string arg);

            [Transpile]
            public class Widget
            {
                public MouseEventListener Listener { get; }

                public Widget(MouseEventListener listener)
                {
                    Listener = listener;
                }

                public void Fire(Element target)
                {
                    Listener(target, "click");
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains(".call(target, \"click\")");
    }

    [Test]
    public async Task EventSubscription_Unaffected_ByInvokeShortcut()
    {
        // Event `+=` lowers to `delegateAdd`, never to a call. The
        // Invoke shortcuts must not interfere.
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

        var output = result["app.ts"];
        await Assert.That(output).Contains("countChanged$add(");
        await Assert.That(output).DoesNotContain("?.()");
    }

    [Test]
    public async Task NullConditionalInvoke_ChainedAccess_PreservesEachShortCircuit()
    {
        // `a?.b?.c?.Invoke()` must short-circuit at every step and
        // still drop the trailing `.Invoke` indirection. The TS form
        // is `a?.b?.c?.()` — optional-call composes with the
        // optional-chain prefix.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Inner
            {
                public Action? Handler;
            }

            [Transpile]
            public class Middle
            {
                public Inner? Inner;
            }

            [Transpile]
            public class Outer
            {
                public Middle? Middle;

                public void Fire()
                {
                    Middle?.Inner?.Handler?.Invoke();
                }
            }
            """
        );

        var output = result["outer.ts"];
        await Assert.That(output).Contains("this.middle?.inner?.handler?.()");
        await Assert.That(output).DoesNotContain("Invoke");
    }

    [Test]
    public async Task NullConditionalInvoke_LowPrecedenceReceiver_ParenthesizesCallee()
    {
        // `(handler ?? fallback)?.Invoke()` lowers to a call whose
        // callee is a binary expression. The printer must wrap it in
        // parens or the output parses as `handler ?? fallback?.()`
        // instead of `(handler ?? fallback)?.()`.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Action? Handler;
                public Action? Fallback;

                public void Fire()
                {
                    (Handler ?? Fallback)?.Invoke();
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("(this.handler ?? this.fallback)?.()");
    }

    [Test]
    public async Task DirectInvoke_LowPrecedenceReceiver_ParenthesizesCallee()
    {
        // `(handler ?? fallback).Invoke()` becomes a plain call whose
        // callee is a binary expression. Without parens the call
        // would bind only to `fallback`.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public Action Handler { get; }
                public Action Fallback { get; }

                public Widget(Action handler, Action fallback)
                {
                    Handler = handler;
                    Fallback = fallback;
                }

                public void Fire()
                {
                    (Handler ?? Fallback).Invoke();
                }
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("(this.handler ?? this.fallback)()");
    }

    [Test]
    public async Task NullConditionalInvoke_DartTarget_LowersToOptionalCallMethod()
    {
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Widget
            {
                public Action<int>? OnTick;

                public void Fire()
                {
                    OnTick?.Invoke(7);
                }
            }
            """
        );

        var dart = files["widget.dart"];
        await Assert.That(dart).Contains("onTick?.call(7)");
        await Assert.That(dart).DoesNotContain("Invoke");
    }

    [Test]
    public async Task DirectInvoke_DartTarget_DropsInvokeIndirection()
    {
        var (files, _) = TranspileDart(
            """
            [Transpile]
            public class Widget
            {
                public Action<int> OnTick { get; }

                public Widget(Action<int> onTick)
                {
                    OnTick = onTick;
                }

                public void Fire()
                {
                    OnTick.Invoke(7);
                }
            }
            """
        );

        var dart = files["widget.dart"];
        await Assert.That(dart).Contains("onTick(7)");
        await Assert.That(dart).DoesNotContain("Invoke");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileDart(string csharpSource)
    {
        var source = $"""
            using System;
            using Metano.Annotations;
            {csharpSource}
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );
        var compilation = CSharpCompilation.Create(
            "DartTestAssembly",
            [syntaxTree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "C# compilation failed:\n" + string.Join("\n", errors.Select(e => e.ToString()))
            );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            TargetLanguage.Dart
        );
        var transformer = new DartTransformer(ir, compilation);
        var files = transformer.TransformAll();
        var printer = new Metano.Dart.Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }
}
