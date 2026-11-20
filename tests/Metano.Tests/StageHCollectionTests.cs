using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests;

/// <summary>Spec 011h factory ownership, mutable-interface exclusions and unsupported calls.</summary>
public class StageHCollectionTests
{
    [Test]
    [Arguments("xs.Select(x => x + 1)", "Select")]
    [Arguments("xs.Where(x => x > 0)", "Where")]
    [Arguments("xs.First()", "First")]
    [Arguments("xs.First(x => x > 0)", "First")]
    [Arguments("xs.FirstOrDefault()", "FirstOrDefault")]
    [Arguments("xs.Any()", "Any")]
    [Arguments("ImmutableArrayExtensions.Select(xs, x => x + 1)", "Select")]
    [Arguments("ImmutableArrayExtensions.First(xs)", "First")]
    public async Task ImmutableArrayExtension_UsesRuntimeLinq(string call, string member)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Immutable;
            using System.Linq;

            [Transpile]
            public static class Calls
            {
                public static object Run(ImmutableArray<int> xs) => {{call}};
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["calls.ts"]).Contains("linq(xs,");

        await Assert
            .That(files["calls.ts"])
            .DoesNotContain($"xs.{char.ToLowerInvariant(member[0])}{member[1..]}(");
    }

    [Test]
    [Arguments("ImmutableArray", "Create(1, 2)", "of([1, 2])")]
    [Arguments("ImmutableArray", "Create(1, 2, 3, 4, 5)", "of([1, 2, 3, 4, 5])")]
    [Arguments("ImmutableList", "Create(1, 2)", "of([1, 2])")]
    [Arguments("ImmutableHashSet", "Create(1, 2)", "of([1, 2])")]
    [Arguments("ImmutableArray", "Create(items)", "from(items)")]
    [Arguments("ImmutableList", "Create(items)", "from(items)")]
    [Arguments("ImmutableHashSet", "Create(items)", "from(items)")]
    [Arguments("ImmutableArray", "CreateRange(items)", "from(items)")]
    [Arguments("ImmutableArray", "Create<int>(span)", "from(span)")]
    public async Task Factory_OnlyOwnsFreshExpandedArguments(
        string family,
        string call,
        string emitted
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Immutable;

            [Transpile]
            public static class Factories
            {
                public static {{family}}<int> Run(int[] items, ReadOnlySpan<int> span) => {{family}}.{{call}};
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert
            .That(files["factories.ts"].Replace("<number>", ""))
            .Contains($"{family}.{emitted}");
    }

    [Test]
    [Arguments(IrCollectionKind.IList)]
    [Arguments(IrCollectionKind.ICollection)]
    [Arguments(IrCollectionKind.ISet)]
    [Arguments(IrCollectionKind.IDictionary)]
    public async Task MutableInterfaceDispatcher_ExcludesImmutableShapes(IrCollectionKind kind)
    {
        await Assert
            .That(RuntimeCollections.ClassesOf(kind).Any(IrCollectionKinds.IsImmutable))
            .IsTrue();

        await Assert
            .That(RuntimeCollections.DispatchClassesOf(kind).Any(IrCollectionKinds.IsImmutable))
            .IsFalse();
    }

    [Test]
    [Arguments("ImmutableArray<int>", "IList<int>")]
    [Arguments("ImmutableList<int>", "ICollection<int>")]
    [Arguments("ImmutableHashSet<int>", "ISet<int>")]
    [Arguments("ImmutableDictionary<string, int>", "IDictionary<string, int>")]
    public async Task ImmutableConversionToMutableInterface_ReportsMs0001(
        string source,
        string target
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Views
            {
                public static {{target}} Run({{source}} value) => value;
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .Count()
            .IsEqualTo(1);
    }

    [Test]
    public async Task UnsupportedImmutableArrayExtension_ReportsAtTheMethodName()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Immutable;
            using System.Linq;

            [Transpile]
            public static class Calls
            {
                public static int Run(ImmutableArray<int> xs) => xs.Aggregate((a, b) => a + b);
            }
            """
        );

        var report = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(report.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo("Aggregate");
        await Assert.That(files["calls.ts"]).Contains("UnsupportedLinqOverload");
    }

    [Test]
    [Arguments("ImmutableArray<int>", "immutableArray", "ImmutableArray.from(items)")]
    [Arguments("ImmutableList<int>", "immutableList", "ImmutableList.from(items)")]
    [Arguments("IImmutableList<int>", "immutableList", "ImmutableList.from(items)")]
    [Arguments("ImmutableHashSet<int>", "immutableHashSet", "ImmutableHashSet.from(items)")]
    [Arguments("IImmutableSet<int>", "immutableHashSet", "ImmutableHashSet.from(items)")]
    [Arguments(
        "ImmutableDictionary<string, int>",
        "immutableDictionary",
        "ImmutableDictionary.empty().setItems(entries)"
    )]
    [Arguments(
        "IImmutableDictionary<string, int>",
        "immutableDictionary",
        "ImmutableDictionary.empty().setItems(entries)"
    )]
    [Arguments("SortedSet<int>", "sortedSet", "new SortedSet(items)")]
    [Arguments(
        "SortedDictionary<string, int>",
        "sortedDictionary",
        "SortedDictionary.assign(entries)"
    )]
    [Arguments("SortedList<string, int>", "sortedList", "SortedList.assign(entries)")]
    [Arguments(
        "ConcurrentDictionary<string, int>",
        "concurrentDictionary",
        "ConcurrentDictionary.assign(entries)"
    )]
    [Arguments("ReadOnlyCollection<int>", "readOnlyList", "new NotSupportedException(")]
    public async Task JsonRoot_UsesItsWireKindAndFactory(string type, string kind, string factory)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;
            using System.Collections.Concurrent;
            using System.Text.Json;
            using System.Text.Json.Serialization;

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

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.InvalidJsonSerializableRoot))
            .IsEmpty();

        await Assert.That(files["context.ts"]).Contains($"kind: \"{kind}\"");
        await Assert.That(files["context.ts"]).Contains(factory);
    }

    [Test]
    [Arguments("ImmutableArray.BinarySearch(xs, 2)", "xs.binarySearch(2)")]
    [Arguments("ImmutableArray.BinarySearch(xs, 2, comparer)", "xs.binarySearch(2, comparer)")]
    [Arguments("xs.BinarySearch(2)", "xs.binarySearch(2)")]
    [Arguments("xs.BinarySearch(2, comparer)", "xs.binarySearch(2, comparer)")]
    public async Task ImmutableArrayBinarySearch_UsesTheValueAsReceiver(string call, string emitted)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Search
            {
                public static int Run(ImmutableArray<int> xs, IComparer<int> comparer) => {{call}};
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["search.ts"]).Contains(emitted);
    }
}
