namespace Metano.Tests;

public class ThrowExpressionTests
{
    [Test]
    public async Task NullCoalescingThrow_LowersToIifeWithThrow()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Widget
            {
                private string? _ctx;

                public string Read() => _ctx ?? throw new System.Exception("missing");
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("?? (() =>");
        await Assert.That(output).Contains("throw new Error(\"missing\")");
        // Catches an IR-node `ToString()` leaking into the printed output
        // — records render as `IrNewExpression { … }`.
        await Assert.That(output).DoesNotContain("IrNewExpression {");
    }

    [Test]
    public async Task ThrowExpression_NestedInTernary_LowersWithIife()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Widget
            {
                public int Pick(bool flag) => flag ? 1 : throw new System.Exception("nope");
            }
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("(() =>");
        await Assert.That(output).Contains("throw new Error(\"nope\")");
        // Catches an IR-node `ToString()` leaking into the printed output
        // — records render as `IrNewExpression { … }`.
        await Assert.That(output).DoesNotContain("IrNewExpression {");
    }
}
