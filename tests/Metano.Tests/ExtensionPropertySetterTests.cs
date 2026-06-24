using static Metano.Tests.Assertions.OutputAssertions;

namespace Metano.Tests;

/// <summary>
/// Stage 2 of #156: extension property setters. Reads stay on
/// <c>name$get(receiver)</c> (covered by <see cref="ExtensionCallSiteTests"/>);
/// writes lower to <c>name$set(receiver, value)</c> with a let-bound
/// receiver-once protection for compound-assignment / increment forms.
/// </summary>
public class ExtensionPropertySetterTests
{
    [Test]
    public async Task ExtensionProperty_SimpleSetter_LowersToSetCall()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Cell
            {
                public int Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public int Value
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run(Cell c) { c.Value = 7; }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("value$set(c, 7)");
        await Assert.That(output).DoesNotContain("c.value =");
    }

    [Test]
    public async Task ExtensionProperty_CompoundAssignment_EvaluatesReceiverOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Cell
            {
                public int Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public int Count
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public Cell Pick() => new Cell();
                public void Run() { Pick().Count += 1; }
            }
            """
        );

        var output = result["app/caller.ts"];
        // Receiver Pick() should appear exactly once — the let binding
        // captures it and both the read and the write reference the temp.
        var pickOccurrences = CountOccurrences(output, "this.pick()");
        await Assert.That(pickOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("count$get(");
        await Assert.That(output).Contains("count$set(");
        // IIFE shape signals the let lowering (no statement extraction).
        // Pin the opener so a future bridge tweak that drops the
        // outer parens fails the test instead of silently passing.
        await Assert.That(output).Contains("((receiver$temp$0) =>");
    }

    [Test]
    public async Task ExtensionProperty_SimpleReceiver_SkipsLetBinding()
    {
        // Identifier receivers are side-effect-free, so the rewrite stays
        // compact — no IIFE, single set call against the existing name.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Cell
            {
                public int Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public int Count
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run(Cell c) { c.Count += 1; }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("count$set(c, count$get(c) + 1)");
        await Assert.That(output).DoesNotContain("receiver$temp$");
    }

    [Test]
    public async Task ExtensionProperty_Increment_EvaluatesReceiverOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Cell
            {
                public int Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public int Count
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public Cell Pick() => new Cell();
                public void Run() { Pick().Count++; }
            }
            """
        );

        var output = result["app/caller.ts"];
        var pickOccurrences = CountOccurrences(output, "this.pick()");
        await Assert.That(pickOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("count$set(receiver$temp$0,");
        await Assert.That(output).Contains("count$get(receiver$temp$0) + 1");
    }

    [Test]
    public async Task ExtensionProperty_NullConditionalAssign_LowersToSetterCall()
    {
        // Receiver is a simple identifier so the null check duplicates it
        // without a let binding — the legacy null-conditional pipeline
        // already enforces that constraint. The inner write routes through
        // the new `$set` helper instead of `obj.value = v`.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Cell
            {
                public int Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public int Value
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run(Cell? c) { c?.Value = 9; }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("value$set(c, 9)");
        await Assert.That(output).Contains("c != null");
    }

    [Test]
    public async Task ExtensionProperty_SetterHelper_EmitsModuleFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Cell
            {
                public int Backing { get; set; }
            }

            [Transpile]
            public static class CellExtensions
            {
                extension(Cell cell)
                {
                    public int Value
                    {
                        get => cell.Backing;
                        set => cell.Backing = value;
                    }
                }
            }
            """
        );

        var output = result["app/cell-extensions.ts"];
        await Assert.That(output).Contains("export function value$get");
        await Assert.That(output).Contains("export function value$set");
        await Assert.That(output).Contains("value: number");
    }
}
