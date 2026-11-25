using static Metano.Tests.Assertions.OutputAssertions;

namespace Metano.Tests;

/// <summary>
/// Stage 4 of #156: extension indexers. Reads lower to
/// <c>item$get(receiver, index)</c>, writes to
/// <c>item$set(receiver, index, value)</c>, and compound forms reuse the
/// Stage 2 <c>IrLetExpression</c> so impure receivers and indices evaluate
/// once. <c>[IndexerName]</c> swaps the helper base name (e.g.,
/// <c>slot$get</c> / <c>slot$set</c>).
/// </summary>
public class ExtensionIndexerTests
{
    [Test]
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

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("item$get(bag, 0)");
        await Assert.That(output).DoesNotContain("bag[0]");
    }

    [Test]
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

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("item$set(bag, 0, 7)");
        await Assert.That(output).DoesNotContain("bag[0] =");
    }

    [Test]
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

        var output = result["app/caller.ts"];
        // Receiver Pick() should appear exactly once — the IIFE captures it.
        var pickOccurrences = CountOccurrences(output, "this.pick()");
        await Assert.That(pickOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("item$get(receiver$temp$0,");
        await Assert.That(output).Contains("item$set(receiver$temp$0,");
        await Assert.That(output).Contains("((receiver$temp$0) =>");
    }

    [Test]
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

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("item$set(bag, 0, (item$get(bag, 0) + 1) | 0)");
        await Assert.That(output).DoesNotContain("receiver$temp$");
    }

    [Test]
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

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("item$set(bag, 0, (item$get(bag, 0) + 1) | 0)");
    }

    [Test]
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

        var caller = result["app/caller.ts"];
        await Assert.That(caller).Contains("slot$get(bag, 0)");
        await Assert.That(caller).Contains("slot$set(bag, 0, 9)");

        var helpers = result["app/bag-extensions.ts"];
        await Assert.That(helpers).Contains("export function slot$get");
        await Assert.That(helpers).Contains("export function slot$set");
    }

    [Test]
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

        var output = result["app/bag-extensions.ts"];
        await Assert.That(output).Contains("export function item$get");
        await Assert.That(output).Contains("export function item$set");
        await Assert.That(output).Contains("value: number");
    }

    [Test]
    public async Task ExtensionIndexer_UsingFile_ImportsGetAndSet()
    {
        var output = TranspileNullableBag("public void Run(Bag bag) { bag[0] = bag[1]; }");

        await Assert
            .That(output)
            .Contains("import { item$get, item$set } from \"./bag-extensions.js\";");
    }

    [Test]
    public async Task ExtensionIndexer_NullableCompoundAssignment_LiftsNull()
    {
        var output = TranspileNullableBag("public void Run(Bag bag) { bag[0] += 2; }");

        await Assert
            .That(output)
            .Contains(
                "item$set(bag, 0, item$get(bag, 0) == null ? null : (item$get(bag, 0)! + 2) | 0)"
            );
    }

    [Test]
    public async Task ExtensionIndexer_NullableIncrement_LiftsNull()
    {
        var output = TranspileNullableBag("public void Run(Bag bag) { bag[0]++; }");

        await Assert
            .That(output)
            .Contains(
                "item$set(bag, 0, item$get(bag, 0) == null ? null : (item$get(bag, 0)! + 1) | 0)"
            );
    }

    [Test]
    public async Task ExtensionIndexer_ImpureIndex_EvaluatesOnce()
    {
        var output = TranspileNullableBag(
            """
            private int _next;
            public int Next() => _next++;
            public void Run(Bag bag) { bag[Next()] += 1; }
            """
        );

        await Assert.That(CountOccurrences(output, "this.next()")).IsEqualTo(1);
    }

    [Test]
    public async Task ExtensionIndexer_UsedIncrement_YieldsPreviousValue()
    {
        var output = TranspileNullableBag(
            "public int? Run(Bag bag) { var x = bag[0]++; return x; }"
        );

        await Assert.That(output).DoesNotContain("const x = item$set(");
        await Assert.That(output).Contains("return receiver$temp$0;");
    }

    [Test]
    public async Task ExtensionIndexer_UsedAssignment_YieldsStoredValue()
    {
        var output = TranspileNullableBag(
            "public int? Run(Bag bag) { var x = bag[0] = 4; return x; }"
        );

        await Assert.That(output).DoesNotContain("const x = item$set(");
        await Assert.That(output).Contains("item$set(bag, 0, 4);");
        await Assert.That(output).Contains("return 4;");
    }

    [Test]
    public async Task ExtensionIndexer_CoalesceAssignment_StoresOnlyWhenNull()
    {
        var output = TranspileNullableBag("public void Run(Bag bag) { bag[1] ??= 5; }");

        await Assert.That(output).Contains("item$get(bag, 1) ?? item$set(bag, 1, 5);");
    }

    [Test]
    public async Task ExtensionIndexer_NullableBooleanCompound_UsesThreeValuedLogic()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            namespace App;

            public class Flags
            {
                public List<bool?> Storage { get; } = new();
            }

            [Transpile]
            public static class FlagsExtensions
            {
                extension(Flags flags)
                {
                    public bool? this[int index]
                    {
                        get => flags.Storage[index];
                        set => flags.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run(Flags flags, bool? other) { flags[0] &= other; }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).DoesNotContain("& other");
        await Assert.That(output).Contains("=== false");
    }

    private static string TranspileNullableBag(string callerMembers)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;
            namespace App;

            public class Bag
            {
                public List<int?> Storage { get; } = new();
            }

            [Transpile]
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int? this[int index]
                    {
                        get => bag.Storage[index];
                        set => bag.Storage[index] = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                {{callerMembers}}
            }
            """
        );

        return result["app/caller.ts"];
    }
}
