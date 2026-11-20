using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests;

/// <summary>H/J integration: bound factory result arguments close every 011h family.</summary>
public class StageHCollectionSlotTests
{
    [Test]
    [Arguments("ImmutableList<Item>", "ImmutableList.empty<Item>([Item.$runtimeType])")]
    [Arguments("ImmutableArray<Item>", "ImmutableArray.empty<Item>([Item.$runtimeType])")]
    [Arguments("ImmutableHashSet<Item>", "ImmutableHashSet.empty<Item>([Item.$runtimeType])")]
    [Arguments(
        "ImmutableDictionary<int, Item>",
        "ImmutableDictionary.empty<number, Item>([Int32Type, Item.$runtimeType])"
    )]
    public async Task LocalStaticEmpty_ReachesElementDescriptor(string collection, string expected)
    {
        var files = TranspileHelper.Transpile(
            $$"""
            using System.Collections.Immutable;
            [Transpile] public class Item { }
            [Transpile] public static class Probe
            {
                public static bool Run()
                {
                    object value = {{collection}}.Empty;
                    return value is {{collection.Replace("Item", "string")}};
                }
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains(expected);
        await Assert.That(files["item.ts"]).Contains("static get $runtimeType()");
    }

    [Test]
    public async Task LocalMemberRead_DoesNotRequestFactorySlot()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            [Transpile] public class Item { }
            [Transpile] public static class Probe
            {
                public static List<Item> Read(KeyValuePair<int, List<Item>> pair) => pair.Value;
                public static bool Test(object value) => value is List<string>;
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("return pair[1]");
        await Assert.That(files["probe.ts"]).DoesNotContain("pair[1], [Item.$runtimeType]");
    }

    [Test]
    [Arguments(
        "ImmutableList<int>",
        "ImmutableList.Create(1, 2)",
        "ImmutableList.of([1, 2], [Int32Type])"
    )]
    [Arguments(
        "ImmutableList<int>",
        "ImmutableList<int>.Empty",
        "ImmutableList.empty([Int32Type])"
    )]
    [Arguments(
        "ImmutableList<int>",
        "items.ToImmutableList()",
        "ImmutableList.from(items, [Int32Type])"
    )]
    [Arguments(
        "ImmutableArray<int>",
        "ImmutableArray.Create(1, 2)",
        "ImmutableArray.of([1, 2], [Int32Type])"
    )]
    [Arguments(
        "ImmutableArray<int>",
        "ImmutableArray<int>.Empty",
        "ImmutableArray.empty([Int32Type])"
    )]
    [Arguments(
        "ImmutableArray<int>",
        "ImmutableArray.Create(items, 0, 1)",
        "ImmutableArray.fromRange(items, 0, 1, [Int32Type])"
    )]
    [Arguments(
        "ImmutableHashSet<int>",
        "ImmutableHashSet.Create(1, 2)",
        "ImmutableHashSet.of([1, 2], null, [Int32Type])"
    )]
    [Arguments(
        "ImmutableHashSet<int>",
        "ImmutableHashSet<int>.Empty",
        "ImmutableHashSet.empty([Int32Type])"
    )]
    [Arguments(
        "ImmutableHashSet<int>",
        "items.ToImmutableHashSet()",
        "toImmutableHashSet(items, [Int32Type])"
    )]
    [Arguments(
        "ImmutableDictionary<int, int>",
        "ImmutableDictionary.Create<int, int>()",
        "ImmutableDictionary.empty([Int32Type, Int32Type])"
    )]
    [Arguments(
        "ImmutableDictionary<int, int>",
        "ImmutableDictionary<int, int>.Empty",
        "ImmutableDictionary.empty([Int32Type, Int32Type])"
    )]
    [Arguments(
        "ImmutableDictionary<int, int>",
        "items.ToImmutableDictionary(x => x, x => x)",
        "null, null, [Int32Type, Int32Type])"
    )]
    [Arguments(
        "ImmutableList<int>.Builder",
        "ImmutableList.CreateBuilder<int>()",
        "new ImmutableListBuilder(null, [Int32Type])"
    )]
    [Arguments(
        "ImmutableArray<int>.Builder",
        "ImmutableArray.CreateBuilder<int>()",
        "new ImmutableArrayBuilder(null, [Int32Type])"
    )]
    [Arguments(
        "ImmutableHashSet<int>.Builder",
        "ImmutableHashSet.CreateBuilder<int>()",
        "new ImmutableHashSetBuilder(null, null, [Int32Type])"
    )]
    [Arguments(
        "ImmutableDictionary<int, int>.Builder",
        "ImmutableDictionary.CreateBuilder<int, int>()",
        "new ImmutableDictionaryBuilder(null, null, null, [Int32Type, Int32Type])"
    )]
    [Arguments("SortedSet<int>", "new SortedSet<int>()", "new SortedSet(null, null, [Int32Type])")]
    [Arguments(
        "SortedDictionary<int, int>",
        "new SortedDictionary<int, int>()",
        "new SortedDictionary(null, null, [Int32Type, Int32Type])"
    )]
    [Arguments(
        "SortedList<int, int>",
        "new SortedList<int, int>(3)",
        "SortedList.withCapacity(3, null, [Int32Type, Int32Type])"
    )]
    [Arguments(
        "ConcurrentDictionary<int, int>",
        "new ConcurrentDictionary<int, int>()",
        "new ConcurrentDictionary(null, null, [Int32Type, Int32Type])"
    )]
    [Arguments(
        "ConcurrentDictionary<int, int>",
        "new ConcurrentDictionary<int, int>(1, 3)",
        "ConcurrentDictionary.withCapacity(1, 3, null, [Int32Type, Int32Type])"
    )]
    [Arguments(
        "ReadOnlyCollection<int>",
        "Array.AsReadOnly(items)",
        "ReadOnlyList.fromArray(items, [Int32Type])"
    )]
    [Arguments("ReadOnlyCollection<int>", "new List<int>().AsReadOnly()", "[Int32Type])")]
    [Arguments(
        "ImmutableList<int>.Builder",
        "ImmutableList.Create(1).ToBuilder()",
        "new ImmutableListBuilder(ImmutableList.of([1]), [Int32Type])"
    )]
    public async Task Factory_ClosesItsBoundResult(string type, string source, string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.Concurrent;
            using System.Collections.ObjectModel;
            using System.Linq;

            [Transpile]
            public static class Probe
            {
                public static Type Describe() => typeof({{type}});
                public static {{type}} Make(int[] items) => {{source}};
            }
            """
        );

        await Assert.That(diagnostics.Where(d => d.Code == "MS0001")).IsEmpty();

        var output = files["probe.ts"].Replace("<number, number>", "").Replace("<number>", "");

        await Assert.That(output).Contains(expected);
    }

    [Test]
    public async Task FactoryArgument_IsReachedFromItsBoundReturnType()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Collections.Immutable;
            [Transpile] public record Item(int Id);
            [Transpile] public static class Probe
            {
                public static bool Is(object value) => value is ImmutableList<string>;
                public static ImmutableList<Item> Make() => ImmutableList.Create(new Item(1));
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("[Item.$runtimeType]");
        await Assert.That(files["item.ts"]).Contains("static get $runtimeType()");
    }

    [Test]
    [Arguments("IList<int>")]
    [Arguments("ISet<int>")]
    [Arguments("IDictionary<int, int>")]
    public async Task MutableInterfaceBindings_StayLoud(string tested)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;
            [Transpile] public static class Probe
            {
                public static {{tested}}? As(object value) => value as {{tested}};
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == "MS0001"))
            .IsEqualTo(tested == "IList<int>" ? 2 : 1);

        await Assert.That(files["probe.ts"]).Contains("Unsupported expression:");
    }

    [Test]
    public async Task ClrNames_RoundTripAllDescriptorKinds()
    {
        foreach (var kind in RuntimeCollections.ClassKinds())
        {
            await Assert
                .That(RuntimeCollections.KindOfClrName(RuntimeCollections.ClrNameOf(kind)))
                .IsEqualTo(kind);

            await Assert.That(RuntimeCollections.DescriptorFactoryOf(kind)).IsNotNull();
        }
    }

    [Test]
    [Arguments("ImmutableList<int>", "ImmutableList.from(items, [Int32Type])")]
    [Arguments("IImmutableList<int>", "ImmutableList.from(items, [Int32Type])")]
    [Arguments("ImmutableArray<int>", "ImmutableArray.from(items, [Int32Type])")]
    [Arguments("ImmutableHashSet<int>", "ImmutableHashSet.from(items, null, [Int32Type])")]
    [Arguments("IImmutableSet<int>", "ImmutableHashSet.from(items, null, [Int32Type])")]
    [Arguments(
        "ImmutableDictionary<string, int>",
        "ImmutableDictionary.empty([StringType, Int32Type]).setItems(entries)"
    )]
    [Arguments(
        "IImmutableDictionary<string, int>",
        "ImmutableDictionary.empty([StringType, Int32Type]).setItems(entries)"
    )]
    [Arguments("SortedSet<int>", "new SortedSet(items, null, [Int32Type])")]
    [Arguments(
        "SortedDictionary<string, int>",
        "SortedDictionary.assign(entries, null, [StringType, Int32Type])"
    )]
    [Arguments(
        "SortedList<string, int>",
        "SortedList.assign(entries, null, [StringType, Int32Type])"
    )]
    [Arguments(
        "ConcurrentDictionary<string, int>",
        "ConcurrentDictionary.assign(entries, null, [StringType, Int32Type])"
    )]
    public async Task JsonCreateClosure_ClosesItsConcreteClass(string type, string expected)
    {
        var files = TranspileHelper.Transpile(
            $$"""
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.Concurrent;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [Transpile] public static class Probe
            {
                public static Type Describe() => typeof({{type}});
            }
            [Transpile]
            [JsonSerializable(typeof({{type}}))]
            public partial class Context : JsonSerializerContext
            {
                public Context() : base(null) { }
                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        await Assert.That(files["context.ts"]).Contains(expected);
    }
}
