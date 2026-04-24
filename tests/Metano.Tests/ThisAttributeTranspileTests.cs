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
