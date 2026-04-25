namespace Metano.Tests;

/// <summary>
/// Regression coverage for issue #120: <c>a?.b = c</c> previously
/// surfaced as <c>IrUnsupportedExpression</c> at extraction time,
/// leaving the statement out of the generated TypeScript. The fix
/// lowers the assignment into an inline short-circuit
/// (<c>a != null &amp;&amp; (a.b = c)</c>) so the null-guard is
/// preserved without a body-walker rewrite.
/// </summary>
public class NullConditionalAssignmentTests
{
    [Test]
    public async Task NullConditionalPropertyAssignment_LowersThroughShortCircuit()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            [Transpile]
            public class Renderer
            {
                private Element? _text;

                public void Update(int counter)
                {
                    _text?.InnerHtml = counter.ToString();
                }
            }
            """
        );

        var output = result["renderer.ts"];
        // Inline short-circuit form: receiver != null && (receiver.member = value)
        await Assert.That(output).Contains("this._text != null && (this._text.innerHtml =");
        // The previous extraction surfaced this as an unsupported
        // node — the diagnostic must be gone for this slice.
        await Assert.That(output).DoesNotContain("Unsupported");
    }

    [Test]
    public async Task NullConditionalAssignment_OnLocal_LowersWithoutThis()
    {
        // Same shape against a local variable instead of `this._text`
        // — the receiver in the lowered expression flows from the
        // identifier directly, not through an implicit `this.`.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Element
            {
                public string InnerHtml { get; set; } = "";
            }

            [Transpile]
            public class Renderer
            {
                public void Update(Element? element)
                {
                    element?.InnerHtml = "ready";
                }
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("element != null && (element.innerHtml = \"ready\")");
    }

    [Test]
    public async Task NullConditionalNestedMemberAssignment_LowersThroughShortCircuit()
    {
        // `a?.b.c = d` parses as ConditionalAccess(a, Assignment(MemberAccess(MemberBinding(b), c), d)).
        // The lowering rebinds the inner MemberBinding root onto the
        // receiver and reuses the same short-circuit shape.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Inner
            {
                public string Value { get; set; } = "";
            }

            [Transpile]
            public class Outer
            {
                public Inner Inner { get; set; } = new();
            }

            [Transpile]
            public class Container
            {
                public Outer? Outer { get; set; }

                public void Set()
                {
                    Outer?.Inner.Value = "x";
                }
            }
            """
        );

        var output = result["container.ts"];
        await Assert
            .That(output)
            .Contains("this.outer != null && (this.outer.inner.value = \"x\")");
    }

    [Test]
    public async Task NullConditionalCompoundAssignment_LowersWithMatchingOperator()
    {
        // `a?.b += c` keeps the original C# operator (`+=`) on the
        // member-write half of the short-circuit so the TS output
        // matches the source intent.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public int Value { get; set; }
            }

            [Transpile]
            public class Pad
            {
                public Counter? Slot { get; set; }

                public void Bump()
                {
                    Slot?.Value += 1;
                }
            }
            """
        );

        var output = result["pad.ts"];
        await Assert.That(output).Contains("this.slot != null && (this.slot.value += 1)");
    }
}
