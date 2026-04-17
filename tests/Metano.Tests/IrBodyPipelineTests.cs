namespace Metano.Tests;

/// <summary>
/// Phase 5.10b integration coverage: drives the full transpile pipeline with the
/// IR-driven method body bridge enabled (via
/// <see cref="TranspileHelper.TranspileWithIrBodies"/>). Pins the output for the
/// shapes the IR extractor currently covers so the class-body IR path stays wired
/// and produces the expected TypeScript lowering. Production samples still use
/// the legacy <c>ExpressionTransformer</c> by default; these tests are the
/// proof that the IR path is plumbed through and lowering behavior matches
/// expectations for the covered subset.
/// </summary>
public class IrBodyPipelineTests
{
    [Test]
    public async Task SimpleReturn_LowersViaIrPath()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            [Transpile]
            public class Adder
            {
                public int Add(int a, int b) => a + b;
            }
            """
        );

        var output = result["adder.ts"];
        await Assert.That(output).Contains("add(a: number, b: number): number");
        await Assert.That(output).Contains("return a + b;");
    }

    [Test]
    public async Task AssignmentAndIf_LowerViaIrPath()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            [Transpile]
            public class Branching
            {
                public int Classify(int x)
                {
                    if (x > 0) return 1;
                    return -1;
                }
            }
            """
        );

        var output = result["branching.ts"];
        await Assert.That(output).Contains("if (x > 0) {");
        await Assert.That(output).Contains("return 1;");
        await Assert.That(output).Contains("return -1;");
    }

    [Test]
    public async Task BclMappingStillApplies_WhenRoutedThroughIr()
    {
        // [MapMethod] for List<T>.Add → push is declared in Metano.Runtime/Lists.cs
        // and picked up by DeclarativeMappingRegistry. The IR body pipeline must
        // plug that registry through IrToTsExpressionBridge so the lowered call
        // goes through the mapping rather than emitting a raw `items.add(value)`.
        // The implicit-`this` on `Items.Add(value)` is expanded to
        // `this.items.push(value)` by the IR extractor's this-synthesis.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;

            [Transpile]
            public class TodoList
            {
                public List<int> Items { get; } = new();
                public void Append(int value) => Items.Add(value);
            }
            """
        );

        var output = result["todo-list.ts"];
        await Assert.That(output).Contains("this.items.push(value)");
    }
}
