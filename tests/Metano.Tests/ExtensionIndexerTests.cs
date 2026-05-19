using static Metano.Tests.Assertions.OutputAssertions;

namespace Metano.Tests;

/// <summary>
/// Stage 4 of #156: extension indexers. Reads lower to
/// <c>item$get(receiver, index)</c>, writes to
/// <c>item$set(receiver, index, value)</c>, and compound forms reuse the
/// Stage 2 <c>IrLetExpression</c> so impure receivers evaluate once.
/// <c>[IndexerName]</c> swaps the helper base name (e.g.,
/// <c>slot$get</c> / <c>slot$set</c>).
/// <para>
/// All tests are currently <see cref="SkipAttribute">skipped</see> because
/// the Roslyn shipped with .NET 10 (Microsoft.CodeAnalysis.CSharp 5.3.0)
/// raises <c>CS9282 "this member is not allowed in an extension block"</c>
/// when an indexer appears inside an <c>extension(R) { … }</c> block, even
/// under <c>LangVersion="preview"</c>. The Stage 4 lowering code paths
/// (<see cref="Metano.Compiler.Extraction.IrModuleFunctionExtractor"/>,
/// <see cref="Metano.Compiler.Extraction.IrExpressionExtractor"/>,
/// <see cref="Metano.Compiler.TypeScript.Transformation.TypeScriptTransformContext"/>)
/// are wired so the rewrites kick in as soon as a future Roslyn release
/// admits the syntax; removing the skips here will then exercise them
/// end-to-end without further code changes.
/// </para>
/// </summary>
public class ExtensionIndexerTests
{
    private const string PendingReason =
        "Roslyn 5.3.0 (.NET 10) rejects indexers in extension blocks (CS9282); "
        + "remove this skip once a Roslyn release admits the syntax.";

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_Read_LowersToItemGet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public int Read(Bag bag) => bag[0];
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("item$get(bag, 0)");
        await Assert.That(output).DoesNotContain("bag[0]");
    }

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_Write_LowersToItemSet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Write(Bag bag) { bag[0] = 7; }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("item$set(bag, 0, 7)");
        await Assert.That(output).DoesNotContain("bag[0] =");
    }

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_CompoundAssignment_ImpureReceiver_EvaluatesOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public Bag Pick() => new Bag();
                public void Run() { Pick()[0] += 1; }
            }
            """
        );

        var output = result["caller.ts"];
        // Receiver Pick() should appear exactly once — the IIFE captures it.
        var pickOccurrences = CountOccurrences(output, "this.pick()");
        await Assert.That(pickOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("item$get(receiver$temp$0,");
        await Assert.That(output).Contains("item$set(receiver$temp$0,");
        await Assert.That(output).Contains("((receiver$temp$0) =>");
    }

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_SimpleReceiver_SkipsLetBinding()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run(Bag bag) { bag[0] += 1; }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("item$set(bag, 0, item$get(bag, 0) + 1)");
        await Assert.That(output).DoesNotContain("receiver$temp$");
    }

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_IncrementStatement_LowersToSetterCall()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run(Bag bag) { bag[0]++; }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("item$set(bag, 0, item$get(bag, 0) + 1)");
    }

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_CustomName_RespectsIndexerName()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    [IndexerName("Slot")]
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public int Read(Bag bag) => bag[0];
                public void Write(Bag bag) { bag[0] = 9; }
            }
            """
        );

        var caller = result["caller.ts"];
        await Assert.That(caller).Contains("slot$get(bag, 0)");
        await Assert.That(caller).Contains("slot$set(bag, 0, 9)");

        var helpers = result["bag-extensions.ts"];
        await Assert.That(helpers).Contains("export function slot$get");
        await Assert.That(helpers).Contains("export function slot$set");
    }

    [Test]
    [Skip(PendingReason)]
    public async Task ExtensionIndexer_HelperFile_EmitsGetAndSet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }
            """
        );

        var output = result["bag-extensions.ts"];
        await Assert.That(output).Contains("export function item$get");
        await Assert.That(output).Contains("export function item$set");
        await Assert.That(output).Contains("value: number");
    }
}
