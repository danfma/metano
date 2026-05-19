namespace Metano.Tests;

/// <summary>
/// Pins the ordering and identity contract for the invocation
/// rewriter chain introduced in #221. The chain replaced an inline
/// symbol-cascade in <c>IrExpressionExtractor.ExtractInvocation</c>;
/// these tests catch regressions that the existing per-feature suites
/// would miss because they target the dispatch boundary, not the
/// lowered shape.
/// </summary>
public class InvocationRewriterCanaryTests
{
    [Test]
    public async Task RegularMethodNamedInvoke_DoesNotGetDelegateInvokeShortcut()
    {
        // The DelegateInvokeRewriter must gate on the method's
        // `MethodKind == DelegateInvoke` synthesised marker — not on
        // the literal name "Invoke". A regular method named Invoke
        // on a class must keep its `.invoke(...)` call segment
        // intact; without the kind gate it would silently lower to
        // a bare function call against the receiver.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Handler
            {
                public int Invoke(int x) => x;

                public int Run() => this.Invoke(42);
            }
            """
        );

        var output = result["handler.ts"];

        await Assert.That(output).Contains("this.invoke(42)");
        // The bare-call form would drop the `.invoke(` segment entirely.
        await Assert.That(output).DoesNotContain("this(42)");
    }

    [Test]
    public async Task DelegateInvoke_LowersToBareCall()
    {
        // The positive half of the canary: a real delegate's
        // synthesised Invoke does still go through the rewriter
        // (matching the legacy behaviour) and lowers without the
        // `.Invoke` indirection.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                public System.Action<int>? OnTick;

                public void Fire() { if (OnTick is not null) OnTick.Invoke(7); }
            }
            """
        );

        var output = result["widget.ts"];

        await Assert.That(output).Contains("this.onTick(7)");
        // The pre-rewrite shape would have surfaced as `.Invoke(...)`
        // in the output — pinning the absence of that exact substring
        // is the canary, not a generic "any 'invoke' is bad" check.
        await Assert.That(output).DoesNotContain(".Invoke(");
    }

    [Test]
    public async Task DecimalParse_LowersToDecimalConstructor()
    {
        // Pins the IntrinsicBclLoweringTable's `decimal.Parse` →
        // `new Decimal(...)` rewrite. The table's symbol-keyed
        // dispatch must resolve every `Parse` overload because the
        // BCL declares several (string, ReadOnlySpan<char>,
        // IFormatProvider, NumberStyles).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public decimal Parse(string s) => decimal.Parse(s);
            }
            """
        );

        var output = result["calc.ts"];

        await Assert.That(output).Contains("new Decimal(s)");
    }

    [Test]
    public async Task MathDecimalRound_LowersToInstanceCall()
    {
        // Pins the table's `Math.{Round,Floor,Ceiling,Abs}(decimal)`
        // dispatch. The table receives the Roslyn method symbol
        // (one per overload) and emits a receiver-instance call
        // because decimal.js exposes the rounding family on the
        // Decimal instance, not on a Math static.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public decimal Whole(decimal x) => System.Math.Round(x);
            }
            """
        );

        var output = result["calc.ts"];

        await Assert.That(output).Contains("x.round()");
        // The pre-rewrite shape would have surfaced as `Math.Round(x)`
        // verbatim (PascalCase, no method translation). Pin the
        // absence of that exact source form, not the generic
        // "Math.round" substring.
        await Assert.That(output).DoesNotContain("Math.Round(x)");
    }

    [Test]
    public async Task EmitAttribute_LowersToInlineTemplate()
    {
        // Pins the AttributeTemplateRewriter's `[Emit]` arm. The
        // template author writes `$0.toFixed($1)` verbatim and the
        // backend must surface it at the call site with the
        // arguments substituted at the matching slots — bypassing
        // the rewriter would emit a regular call to a `.toFixed`
        // helper that doesn't exist.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Helpers
            {
                [Emit("$0.toFixed($1)")]
                public static extern string Format(decimal value, int digits);

                public static string Show(decimal m) => Format(m, 2);
            }
            """
        );

        var output = result["helpers.ts"];

        await Assert.That(output).Contains("m.toFixed(2)");
    }

    [Test]
    public async Task ImportAttribute_LowersToFacadeCallWithImport()
    {
        // Pins the AttributeTemplateRewriter's `[Import]`-only arm.
        // No `[Emit]` template means the rewriter synthesises one of
        // the form `name($0, $1, …)` AND emits the matching ESM
        // import line so the consumer file resolves the helper.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Sums
            {
                [Import(name: "add", from: "math-utils")]
                public static extern int Add(int a, int b);

                public static int Total(int a, int b) => Add(a, b);
            }
            """
        );

        var output = result["sums.ts"];

        await Assert.That(output).Contains("add(a, b)");
        await Assert.That(output).Contains("from \"math-utils\"");
    }

    [Test]
    public async Task EmitAndImportTogether_EmitTemplateWinsAndImportThreads()
    {
        // Precedence canary: when a method carries both [Emit] and
        // [Import], the emit template takes the lowered shape AND
        // the import is threaded as an external dependency. Reverse
        // the rewriter's internal order and the call would lower as
        // a facade `name(…)` instead of the bespoke template.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class HashUtils
            {
                [Emit("hashBytes($0)"), Import(name: "hashBytes", from: "crypto-utils")]
                public static extern int Hash(byte[] data);

                public static int Run(byte[] b) => Hash(b);
            }
            """
        );

        var output = result["hash-utils.ts"];

        await Assert.That(output).Contains("hashBytes(b)");
        await Assert.That(output).Contains("from \"crypto-utils\"");
    }

    [Test]
    public async Task InlineMethod_LowersThroughRewriterChain()
    {
        // Pins the InlineMethodExpansionRewriter slot. An [Inline]
        // method call must β-reduce its body at the call site —
        // bypassing the rewriter would emit a regular method call,
        // and since [Inline] methods carry [NoEmit]-like semantics
        // (no body lowered), the call would dangle against a
        // non-existent helper.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, NoContainer]
            public static class MathHelpers
            {
                [Inline(InlineMode.Substitute)]
                public static int Double(int x) => x + x;

                public static int Run(int n) => Double(n);
            }
            """
        );

        var output = result["math-helpers.ts"];

        await Assert.That(output).Contains("return n + n;");
    }
}
