namespace Metano.Tests;

/// <summary>
/// Tests for User Story 4 (composition) of feature 003-js-interop-primitives —
/// contract S1. Proves the three primitives compose into idiomatic destructured
/// signal code with ZERO <c>[Emit]</c> templates, using a self-contained inline
/// binding (NOT the dependent branch-002 SolidJS binding):
/// <list type="bullet">
///   <item><c>[JsTuple, Import]</c> <c>Signal&lt;T&gt;</c> — erased, resolves to
///   Solid's tuple; <c>var (count, setCount) = …</c> destructures the array.</item>
///   <item><c>[JsCallable]</c> <c>ISignalSetter&lt;T&gt;</c> — erased; overloaded
///   <c>Invoke</c> lowers to a direct call.</item>
///   <item><c>[Import]</c> factory <c>Solid.CreateSignal</c> — lowers to
///   <c>createSignal(0)</c> with the <c>solid-js</c> import.</item>
/// </list>
/// </summary>
public class SignalCompositionTests
{
    // Contract S1 — idiomatic destructured signal, zero [Emit].
    [Test]
    public async Task SignalComposition_ProducesIdiomaticDestructuredSignal()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple, Import("Signal", from: "solid-js")]
            public record Signal<T>(System.Func<T> Getter, ISignalSetter<T> Setter);

            [Transpile, JsCallable]
            public interface ISignalSetter<T>
            {
                void Invoke(T value);
                void Invoke(System.Func<T, T> updater);
            }

            [Transpile, NoContainer]
            public static class Solid
            {
                [Import("createSignal", from: "solid-js")]
                public static Signal<T> CreateSignal<T>(T initial) => throw new System.NotSupportedException();
            }

            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main()
                {
                    var (count, setCount) = Solid.CreateSignal(0);
                    System.Console.WriteLine(count());
                    setCount.Invoke(count() + 1);
                    setCount.Invoke(c => c + 1);
                }
            }
            """
        );

        var output = result["program.ts"];
        var expected = TranspileHelper.ReadExpected("signal-composition.ts");
        await Assert.That(output).IsEqualTo(expected);

        // Erased types: no Signal / ISignalSetter declaration files, no wrapper.
        await Assert.That(result.ContainsKey("signal.ts")).IsFalse();
        await Assert.That(result.ContainsKey("i-signal-setter.ts")).IsFalse();
        await Assert.That(output).DoesNotContain("class Signal");
        await Assert.That(output).DoesNotContain("interface ISignalSetter");

        // The createSignal import is present; the call is a bare factory call.
        await Assert.That(output).Contains("import { createSignal } from \"solid-js\"");
        await Assert.That(output).Contains("const [count, setCount] = createSignal(0);");

        // No [Emit]-style index forms anywhere — count/setCount are bound names.
        await Assert.That(output).DoesNotContain("count[0]");
        await Assert.That(output).DoesNotContain("count[1]");
        await Assert.That(output).DoesNotContain("setCount[0]");
        await Assert.That(output).DoesNotContain("setCount[1]");

        // count is a Func<T> → call; setCount is [JsCallable] → direct call,
        // both invocation forms (value + updater) lowered without `.Invoke`.
        await Assert.That(output).Contains("console.log(count());");
        await Assert.That(output).Contains("setCount(count() + 1);");
        await Assert.That(output).Contains("setCount((c: number) => c + 1);");
        await Assert.That(output).DoesNotContain(".Invoke");
        await Assert.That(output).DoesNotContain(".invoke");
    }
}
