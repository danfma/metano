using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for the <c>[JsCallable]</c> attribute (User Story 1, feature
/// 003-js-interop-primitives). A decorated interface models an erased JS
/// callable value: calls to its <c>Invoke(…)</c> member(s) lower to direct
/// invocation of the receiver (<c>recv.Invoke(args)</c> → <c>recv(args)</c>),
/// overloaded <c>Invoke</c> included. The interface emits no declaration.
/// Misuse (non-interface, or a non-<c>Invoke</c> member) raises <c>MS0028</c>.
/// </summary>
public class JsCallableTranspileTests
{
    // Contract C1 — single-signature Invoke → direct call.
    [Test]
    public async Task SingleInvoke_LowersToDirectCall()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface IAction { void Invoke(int value); }

            [Transpile]
            public class Caller
            {
                public void Fire(IAction cb) => cb.Invoke(5);
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("cb(5)");
        await Assert.That(output).DoesNotContain(".Invoke");
        await Assert.That(output).DoesNotContain(".invoke");
    }

    // Contract C2 — overloaded Invoke (value + updater) both lower to a call.
    [Test]
    public async Task OverloadedInvoke_BothLowerToDirectCall()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface ISignalSetter<T>
            {
                void Invoke(T value);
                void Invoke(Func<T, T> updater);
            }

            [Transpile]
            public class SignalConsumer
            {
                public void Go(ISignalSetter<int> setCount)
                {
                    setCount.Invoke(5);
                    setCount.Invoke(c => c + 1);
                }
            }
            """
        );

        var output = result["signal-consumer.ts"];
        var expected = TranspileHelper.ReadExpected("js-callable-overloaded-invoke.ts");
        await Assert.That(output).IsEqualTo(expected);
    }

    // Contract C3 — arbitrary arity.
    [Test]
    public async Task ArbitraryArity_LowersToDirectCall()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface I3 { void Invoke(int a, string b, bool c); }

            [Transpile]
            public class Caller3
            {
                public void Fire(I3 f) => f.Invoke(1, "x", true);
            }
            """
        );

        var output = result["caller3.ts"];
        await Assert.That(output).Contains("f(1, \"x\", true)");
    }

    // Contract C4 — the [JsCallable] interface emits no declaration file.
    [Test]
    public async Task JsCallableInterface_EmitsNoDeclaration()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface IAction { void Invoke(int value); }

            [Transpile]
            public class Caller
            {
                public void Fire(IAction cb) => cb.Invoke(5);
            }
            """
        );

        // No interface file, and no import of the erased type anywhere.
        await Assert.That(result.ContainsKey("i-action.ts")).IsFalse();
        await Assert.That(result["caller.ts"]).DoesNotContain("import type { IAction }");
    }

    // Contract C5 — [JsCallable] on a non-interface. The attribute's
    // [AttributeUsage(AttributeTargets.Interface)] makes the C# compiler itself
    // reject application to a class (CS0592), so the non-interface arm of
    // MS0028 is unreachable through ordinary source — the constraint is already
    // enforced one layer up. This test pins that the C# compilation fails
    // (the Metano MS0028 non-interface branch stays as defense-in-depth for any
    // future reflection-built / metadata-only symbol).
    [Test]
    public async Task JsCallableOnNonInterface_RejectedByCSharpCompiler()
    {
        await Assert
            .That(() =>
                TranspileHelper.TranspileWithDiagnostics(
                    """
                    using Metano.Annotations.TypeScript;

                    [Transpile, JsCallable]
                    public class C { }
                    """
                )
            )
            .Throws<InvalidOperationException>();
    }

    // Contract C5 — [JsCallable] interface with a non-Invoke member → MS0028.
    [Test]
    public async Task JsCallableInterfaceWithNonInvokeMember_RaisesMS0028()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface I
            {
                void Invoke(int x);
                int Other();
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidJsCallable))
            .IsTrue();
    }

    // Contract C5 (negative) — overloaded Invoke alone raises nothing.
    [Test]
    public async Task JsCallableInterfaceWithOverloadedInvoke_NoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface I
            {
                void Invoke(int x);
                void Invoke(string s);
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidJsCallable))
            .IsFalse();
    }
}
