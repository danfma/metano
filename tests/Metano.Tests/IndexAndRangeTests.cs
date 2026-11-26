using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C# 8 index-from-end (<c>x[^n]</c>) and range (<c>x[a..b]</c>) element
/// accesses (legacy FR-014, expressions). <c>^n</c> lowers to
/// <c>x[x.length - n]</c> — typed like the C# indexer under
/// <c>tsc --strict</c>, unlike <c>x.at(-n)</c>'s <c>T | undefined</c> — and a
/// range to <c>x.slice(start, end)</c>. A receiver with side effects is
/// evaluated once through a let-binding IIFE. Constructs outside that subset
/// report MS0001 and emit a placeholder that throws, never crash the build
/// (#263).
/// </summary>
public class IndexAndRangeTests
{
    [Test]
    public async Task IndexAndRangeAccesses_MatchGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Sequences
            {
                private readonly List<int> _items = [7, 8, 9];

                public int LastOfArray(int[] values) => values[^1];

                public int FromEndOfList(List<int> values, int offset) => values[^offset];

                public int FromEndOfReadOnlyList(IReadOnlyList<int> values) => values[^(1 + 1)];

                public int FromEndOfIList(IList<int> values) => values[^1];

                public char LastChar(string text) => text[^1];

                public int LastItem() => _items[^1];

                public int[] Middle(int[] values) => values[1..^1];

                public string WithoutLast(string text) => text[..^1];

                public List<int> Tail(List<int> values) => values[1..];

                public int[] Copy(int[] values) => values[..];

                public int[] Between(int[] values, int start, int end) => values[start..end];
            }
            """
        );

        await TranspileHelper.AssertGolden("index-and-range.ts", result["sequences.ts"]);
    }

    [Test]
    public async Task IndexFromEnd_WithAwaitedOffsetOnCallReceiver_PassesTheOffsetAfterTheReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public class Sequences
            {
                private static int[] Make() => [1, 2, 3];

                public async Task<int> FromEnd(Task<int> offset) => Make()[^(await offset)];
            }
            """
        );

        await Assert
            .That(result["sequences.ts"])
            .Contains("return fromEnd(Sequences.make(), await offset);");
    }

    [Test]
    public async Task IndexFromEnd_OnCallReceiver_EvaluatesReceiverOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Feed
            {
                private int[] Load() => [1, 2, 3];

                public int Latest() => Load()[^1];
            }
            """
        );

        await Assert.That(result["feed.ts"]).Contains("return fromEnd(this.load(), 1);");
    }

    [Test]
    public async Task Range_OnCallReceiver_EvaluatesReceiverOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Feed
            {
                private int[] Load() => [1, 2, 3];

                public int[] Inner() => Load()[1..^1];
            }
            """
        );

        await Assert
            .That(result["feed.ts"])
            .Contains("return ($r = this.load(), $r.slice(1, $r.length - 1));");
    }

    [Test]
    public async Task IndexFromEnd_OffsetWithSideEffects_RunsBeforeLengthRead()
    {
        // C# evaluates the offset before reading Count: PopL() shrinks the list
        // first, so Peek() returns 50 and Inner() returns [20, 30, 40]. The
        // offset is let-bound so the TypeScript reads `length` afterwards too
        // (verified against `dotnet run` and bun with the same inputs).
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Shrinking
            {
                public List<int> Items { get; } = [10, 20, 30, 40, 50, 60];

                public int PopL()
                {
                    Items.RemoveAt(Items.Count - 1);
                    return 1;
                }

                public int Peek() => Items[^PopL()];

                public List<int> Inner() => Items[1..^PopL()];
            }
            """
        );

        var output = result["shrinking.ts"];

        await Assert.That(output).Contains("return this.items.fromEnd(this.popL());");

        await Assert
            .That(output)
            .Contains("return ($e = this.popL(), this.items.slice(1, this.items.count - $e));");
    }

    [Test]
    public async Task Range_WithoutFromEndBound_DoesNotBindReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Feed
            {
                private int[] Load() => [1, 2, 3];

                public int[] Rest() => Load()[1..];
            }
            """
        );

        await Assert.That(result["feed.ts"]).Contains("return this.load().slice(1);");
    }

    [Test]
    public async Task IndexFromEnd_AsWriteTarget_KeepsAssignmentShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Buffer
            {
                public void Touch(int[] values)
                {
                    values[^1] = 5;
                    values[^2] += 1;
                    values[^1]++;
                }
            }
            """
        );

        var output = result["buffer.ts"];
        await Assert.That(output).Contains("values[values.length - 1] = 5;");

        await Assert
            .That(output)
            .Contains("values[values.length - 2] = (values[values.length - 2]! + 1) | 0;");

        await Assert
            .That(output)
            .Contains("values[values.length - 1] = (values[values.length - 1]! + 1) | 0;");
    }

    [Test]
    public async Task IndexFromEnd_WriteThroughCallReceiver_ReportsUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Buffer
            {
                private int[] Load() => [1, 2, 3];

                public void Touch() { Load()[^1] = 5; }
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "Load()[^1]");
        // The placeholder replaces the whole assignment: a call is not an
        // assignable location, so `(...)() = 5` would not parse.
        await Assert.That(files["buffer.ts"]).DoesNotContain(")() = 5");

        await Assert
            .That(files["buffer.ts"])
            .Contains("throwError(new Error(\"Unsupported expression: IndexFromEndWrite\"))");
    }

    [Test]
    public async Task IndexVariable_ReportsUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class Picker
            {
                public int Pick(int[] values, Index index) => values[index];
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "values[index]");

        await Assert
            .That(files["picker.ts"])
            .Contains("throwError(new Error(\"Unsupported expression: IndexElementAccess\"))");
    }

    [Test]
    public async Task RangeVariable_ReportsUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class Picker
            {
                public int[] Pick(int[] values, Range range) => values[range];
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "values[range]");

        await Assert
            .That(files["picker.ts"])
            .Contains("throwError(new Error(\"Unsupported expression: RangeElementAccess\"))");
    }

    [Test]
    public async Task Range_OverUserDefinedSlice_ReportsUnsupported()
    {
        // A user type's Slice(start, length) is not the target's slice(start, end).
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Window
            {
                public int Count => 3;

                public Window Slice(int start, int length) => this;
            }

            [Transpile]
            public class Viewer
            {
                public Window Inner(Window window) => window[1..^1];
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "window[1..^1]");
        await Assert.That(diagnostics[0].Message).Contains("custom Slice(start, length)");
    }

    [Test]
    public async Task IndexFromEnd_RefArgumentThroughCallReceiver_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Buffer
            {
                private int[] Load() => [1, 2, 3];

                private static void Bump(ref int value) => value++;

                public void Touch() => Bump(ref Load()[^1]);
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        // One for the index that cannot be bound once, one for the `ref` argument
        // itself, which is passed by value (#265).
        await Assert.That(unsupported).Count().IsEqualTo(2);
        await Assert.That(unsupported).Contains(m => m.StartsWith("'ref' argument 'Load()[^1]'"));

        await Assert
            .That(unsupported)
            .Contains(m => !m.StartsWith("'ref'") && m.Contains("Load()[^1]"));
    }

    [Test]
    public async Task StandaloneIndexFromEnd_ReportsUnsupportedInsteadOfCrashing()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class Picker
            {
                public Index Last() => ^1;
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "^1");

        await Assert
            .That(files["picker.ts"])
            .Contains("throwError(new Error(\"Unsupported expression: IndexExpression\"))");
    }

    [Test]
    public async Task UnsupportedExpressionKind_ReportsUnsupportedWithPlaceholder()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Sizes
            {
                public int IntSize() => sizeof(int);
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "sizeof(int)");

        await Assert
            .That(files["sizes.ts"])
            .Contains("throwError(new Error(\"Unsupported expression: SizeOfExpression\"))");
    }

    [Test]
    public async Task IndexFromEnd_InModuleFunction_StaysOnIrPath()
    {
        // The fromEnd helper call must count as covered, or the whole
        // [ExportedAsModule] type would be skipped as unsupported.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Feeds
            {
                public static int Latest(int[][] groups) => groups[0][^1];
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await Assert.That(files["feeds.ts"]).Contains("return fromEnd(groups[0]!, 1);");
    }

    [Test]
    public async Task Dart_LowersIndexFromEndAndRanges()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public class Sequences
            {
                private int[] Load() => [1, 2, 3];

                public int Last(int[] values, int offset) => values[^(offset + 1)];

                public int Latest() => Load()[^1];

                public int[] Middle(int[] values) => values[1..^1];

                public string WithoutLast(string text) => text[..^1];

                public int LastOfList(System.Collections.Generic.List<int> items) => items[^1];
            }
            """
        );

        var output = files["sequences.dart"];
        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(output).Contains("values[values.length - (offset + 1)]");

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) => receiver$temp$0[receiver$temp$0.length - 1])(this.load())"
            );

        await Assert.That(output).Contains("values.sublist(1, values.length - 1)");
        await Assert.That(output).Contains("text.substring(0, text.length - 1)");
        await Assert.That(output).Contains("items[items.length - 1]");
    }

    /// <summary>
    /// A collection access that needs temporaries, in a position with no
    /// statement list to declare them in (a static field initializer), reports
    /// MS0001 at the access itself (spec 011 contracts/runtime-collections.md §
    /// Indexer compound assignments). An instance initializer is a constructor
    /// statement, so it lowers (W10-12).
    /// </summary>
    [Test]
    public async Task CollectionAccessWithoutStatementList_ReportsAtTheAccess()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Holder
            {
                private static int _next;

                private static readonly List<int> Items = [1, 2, 3];

                private static readonly IList<int> Shared = new List<int> { 1, 2, 3 };

                private static readonly int _last = Shared[^Next()];

                private static readonly int _count = Items[..^Next()].Count;

                private static readonly int _bumped = Shared[Next()] += 1;

                private static readonly int _stored = Items[Next()] = 5;

                private static int Next() => _next++;
            }
            """
        );

        // The helper prepends three lines of usings: zero-based, each line is three below its source.
        var lines = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Location?.GetLineSpan().StartLinePosition.Line)
            .ToList();

        await Assert.That(lines).IsEquivalentTo(new int?[] { 14, 16, 18, 20 });
    }

    [Test]
    public async Task CollectionAccessInAnInstanceInitializer_UsesStoreTemporaries()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Holder
            {
                private static int _next;

                private static readonly IList<int> Shared = new List<int> { 1, 2, 3 };

                public int Bumped = Shared[Next()] += 1;

                private static int Next() => _next++;
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsFalse();

        await Assert
            .That(files["holder.ts"])
            .Contains(
                """
                  constructor() {
                    {
                      let $i, $o;
                      this.bumped = ($i = Holder.next(), Holder.shared.set($i, $o = (Holder.shared.get($i) + 1) | 0), $o);
                    }
                  }
                """
            );
    }

    private static async Task AssertSingleUnsupported(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string expression
    )
    {
        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert.That(unsupported[0].Message).Contains(expression);
        await Assert.That(unsupported[0].Location).IsNotNull();
    }
}
