namespace Metano.Tests;

/// <summary>
/// Tests for postfix and prefix increment/decrement (<c>x++</c>, <c>++x</c>, <c>x--</c>,
/// <c>--x</c>). Both forms exist in JS with the same syntax, so the lowering is mostly
/// identity — the postfix variant just needs a dedicated AST node since the printer's
/// generic unary case always renders the operator on the left.
/// </summary>
public class IncrementExpressionTests
{
    [Test]
    public async Task PostfixIncrement_LowersAsIs()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                private int _next = 0;
                public int Next() { var id = _next++; return id; }
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("((this._next = (this._next + 1) | 0) - 1) | 0");
    }

    [Test]
    public async Task PostfixDecrement_LowersAsIs()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                private int _n = 10;
                public void Tick() { _n--; }
            }
            """
        );

        await Assert.That(result["counter.ts"]).Contains("this._n = (this._n - 1) | 0");
    }

    [Test]
    public async Task PrefixIncrement_LowersAsIs()
    {
        // Prefix already worked through TransformPrefixUnary; this is the regression
        // guard so the new postfix path doesn't break it.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                private int _n = 0;
                public int Bump() => ++_n;
            }
            """
        );

        await Assert.That(result["counter.ts"]).Contains("this._n = (this._n + 1) | 0");
    }

    [Test]
    public async Task PostfixInExpressionContext_PreservesOrdering()
    {
        // Confirms the value-then-bump order: assigning x++ to a variable captures the
        // pre-increment value (matching C# semantics, which JS also implements).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Bag
            {
                private int _id = 5;
                public int Take() => _id++;
            }
            """
        );

        await Assert.That(result["bag.ts"]).Contains("((this._id = (this._id + 1) | 0) - 1) | 0");
    }
}
