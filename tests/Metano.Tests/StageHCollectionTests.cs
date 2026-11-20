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

    [Test]
    [Arguments("xs.BinarySearch(1, 3, 2)", "xs.binarySearchRange(1, 3, 2)")]
    [Arguments("xs.BinarySearch(1, 3, 2, comparer)", "xs.binarySearchRange(1, 3, 2, comparer)")]
    [Arguments("ImmutableArray.BinarySearch(xs, 1, 3, 2)", "xs.binarySearchRange(1, 3, 2)")]
    [Arguments(
        "ImmutableArray.BinarySearch(xs, 1, 3, 2, comparer)",
        "xs.binarySearchRange(1, 3, 2, comparer)"
    )]
    [Arguments("xs.Slice(1, 3).Length", "xs.getRange(1, 3).count")]
    public async Task ImmutableArrayIndexLengthForms_MapToTheRangeMembers(
        string call,
        string emitted
    )
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

    [Test]
    [Arguments("ImmutableList.CreateBuilder<int>()", "b.IndexOf(1, 2, 3)", "b.indexOf(1, 2, 3)")]
    [Arguments("ImmutableList.CreateBuilder<int>()", "b.LastIndexOf(1, 2)", "b.lastIndexOf(1, 2)")]
    [Arguments("ImmutableArray.CreateBuilder<int>()", "b.IndexOf(1, 2, 3)", "b.indexOf(1, 2, 3)")]
    [Arguments(
        "ImmutableArray.CreateBuilder<int>()",
        "b.LastIndexOf(1, 2, 3)",
        "b.lastIndexOf(1, 2, 3)"
    )]
    public async Task ImmutableBuilderIndexOfRange_KeepsStartIndexAndCount(
        string create,
        string call,
        string emitted
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Immutable;

            [Transpile]
            public static class Search
            {
                public static int Run()
                {
                    var b = {{create}};
                    return {{call}};
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["search.ts"]).Contains(emitted);
    }

    [Test]
    public async Task ImmutableArrayBuilderIndexOfWithAComparer_IsReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Search
            {
                public static int Run(IEqualityComparer<int> comparer)
                {
                    var b = ImmutableArray.CreateBuilder<int>();
                    return b.IndexOf(1, 0, comparer);
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsNotEmpty();
    }

    [Test]
    [Arguments("ImmutableList.ToImmutableList(src)", "ImmutableList.from<number>(src)")]
    [Arguments("ImmutableArray.ToImmutableArray(src)", "ImmutableArray.from<number>(src)")]
    [Arguments("ImmutableHashSet.ToImmutableHashSet(src)", "ImmutableHashSet.from<number>(src)")]
    [Arguments(
        "ImmutableHashSet.ToImmutableHashSet(src, null)",
        "ImmutableHashSet.from<number>(src, null)"
    )]
    [Arguments("src.ToImmutableHashSet(null)", "ImmutableHashSet.from<number>(src, null)")]
    [Arguments(
        "ImmutableDictionary.ToImmutableDictionary(pairs)",
        "ImmutableDictionary.from(pairs)"
    )]
    [Arguments(
        "ImmutableDictionary.ToImmutableDictionary(src, x => x)",
        "ImmutableDictionary.fromSelectors(src,"
    )]
    [Arguments("ImmutableDictionary.Contains(map, 1, 2)", "map.contains([1, 2])")]
    [Arguments("map.Contains(1, 2)", "map.contains([1, 2])")]
    [Arguments("ImmutableList.Remove(list, 1)", "list.remove(1)")]
    [Arguments("ImmutableList.RemoveRange(list, src)", "list.removeItems(src)")]
    [Arguments("ImmutableList.Replace(list, 1, 2)", "list.replace(1, 2)")]
    [Arguments("list.Remove(1)", "list.remove(1)")]
    public async Task ImmutableExtensionStaticForm_TakesTheFirstArgumentAsReceiver(
        string call,
        string emitted
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Calls
            {
                public static object Run(
                    int[] src,
                    KeyValuePair<int, int>[] pairs0,
                    IImmutableList<int> list,
                    IImmutableDictionary<int, int> map
                )
                {
                    IEnumerable<KeyValuePair<int, int>> pairs = pairs0;
                    return {{call}};
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["calls.ts"]).Contains(emitted);
    }

    [Test]
    [Arguments("IImmutableList<int> x = [1, 2, 3];", "ImmutableList.of<number>(")]
    [Arguments("IImmutableSet<int> x = [3, 2, 3, 1];", "ImmutableHashSet.of<number>(")]
    [Arguments("IImmutableList<int> x = [];", "ImmutableList.")]
    public async Task CollectionExpressionOfAnImmutableInterface_BuildsTheImmutableClass(
        string declaration,
        string emitted
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Immutable;

            [Transpile]
            public static class Build
            {
                public static object Run()
                {
                    {{declaration}}
                    return x;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["build.ts"]).Contains(emitted);
    }

    [Test]
    [Arguments("object o", "if (o is ISet<int> s) { s.Add(1); }", 1)]
    [Arguments("object o", "var s = o as ISet<int>;", 1)]
    [Arguments("object o", "if (o is IDictionary<string, int> d) { d.Clear(); }", 1)]
    [Arguments("object o", "var n = o switch { IDictionary<string, int> d => 1, _ => 0 };", 1)]
    [Arguments("IReadOnlySet<int> o", "var s = o as ISet<int>;", 1)]
    [Arguments("object o", "var isSet = o is ISet<int>;", 0)]
    [Arguments("object o", "var isMap = o is IDictionary<string, int>;", 0)]
    [Arguments("HashSet<int> o", "var s = o as ISet<int>;", 0)]
    [Arguments("ISet<int> o", "var s = o as ISet<int>;", 0)]
    public async Task ImmutableBindingThroughMutableInterface_IsReportedOnlyWhenItBinds(
        string parameter,
        string statement,
        int reports
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Tests
            {
                public static void Run({{parameter}})
                {
                    {{statement}}
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .Count()
            .IsEqualTo(reports);
    }

    [Test]
    [Arguments("ImmutableList<int>", "IList<int>", "Mutate(value)", 1)]
    [Arguments("ImmutableHashSet<int>", "ISet<int>", "Mutate(value)", 1)]
    [Arguments("ImmutableDictionary<int, int>", "IDictionary<int, int>", "Mutate(value)", 1)]
    [Arguments("List<int>", "IList<int>", "Mutate(value)", 0)]
    [Arguments("ImmutableList<int>", "IReadOnlyList<int>", "Mutate(value)", 0)]
    public async Task GenericConstraintToAMutableInterface_ReportsTheImmutableTypeArgument(
        string argument,
        string constraint,
        string call,
        int reports
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Tests
            {
                static void Mutate<T>(T value) where T : {{constraint}} { }

                public static void Run({{argument}} value)
                {
                    {{call}};
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .Count()
            .IsEqualTo(reports);
    }

    [Test]
    [Arguments("b.Count = 1;")]
    [Arguments("b.Count += 1;")]
    [Arguments("b.Count++;")]
    public async Task ImmutableArrayBuilderCountWrite_IsReported(string statement)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Immutable;

            [Transpile]
            public static class Tests
            {
                public static int Run()
                {
                    var b = ImmutableArray.CreateBuilder<int>();
                    {{statement}}
                    return b.Count;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .Count()
            .IsEqualTo(1);
    }

    [Test]
    public async Task ImmutableListBuilderCountRead_IsNotReported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Immutable;

            [Transpile]
            public static class Tests
            {
                public static int Run()
                {
                    var b = ImmutableArray.CreateBuilder<int>();
                    return b.Count;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["tests.ts"]).Contains("b.count");
    }

    [Test]
    [Arguments("ImmutableHashSet.ToImmutableHashSet(hb)", "toImmutableHashSet(hb)")]
    [Arguments("hb.ToImmutableHashSet()", "toImmutableHashSet(hb)")]
    [Arguments("ImmutableDictionary.ToImmutableDictionary(db)", "toImmutableDictionary(db)")]
    [Arguments("db.ToImmutableDictionary()", "toImmutableDictionary(db)")]
    public async Task ImmutableBuilderToImmutableExtension_KeepsTheBuildersComparers(
        string call,
        string emitted
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Immutable;

            [Transpile]
            public static class Calls
            {
                public static object Run(
                    ImmutableHashSet<string>.Builder hb,
                    ImmutableDictionary<string, int>.Builder db
                ) => {{call}};
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["calls.ts"]).Contains(emitted);
    }

    [Test]
    [Arguments("object o", "var s = (ISet<int>)o;", 1)]
    [Arguments("IReadOnlySet<int> o", "var s = (ISet<int>)o;", 1)]
    [Arguments("IImmutableSet<int> o", "var s = (ISet<int>)o;", 1)]
    [Arguments("object o", "var d = (IDictionary<string, int>)o;", 1)]
    [Arguments("IReadOnlyDictionary<string, int> o", "var d = (IDictionary<string, int>)o;", 1)]
    [Arguments("IImmutableDictionary<string, int> o", "var d = (IDictionary<string, int>)o;", 1)]
    [Arguments("HashSet<int> o", "var s = (ISet<int>)o;", 0)]
    [Arguments("ISet<int> o", "var s = (ISet<int>)o;", 0)]
    public async Task ExplicitCastToAMutableSetOrDictionary_IsReportedWhenAnImmutableCanBeThere(
        string parameter,
        string statement,
        int reports
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Tests
            {
                public static void Run({{parameter}})
                {
                    {{statement}}
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .Count()
            .IsEqualTo(reports);
    }

    [Test]
    [Arguments("Action<ImmutableList<int>> fn = Mutate<ImmutableList<int>>;", 1)]
    [Arguments("Action<List<int>> fn = Mutate<List<int>>;", 0)]
    [Arguments("var f = Box<ImmutableList<int>>.Make;", 1)]
    [Arguments("Mutate<ImmutableList<int>>(null!);", 1)]
    public async Task ConstraintBoundByAMethodGroup_IsReported(string statement, int reports)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;

            public class Box<T> where T : IList<int>
            {
                public static void Make(T value) { }
            }

            [Transpile]
            public static class Tests
            {
                static void Mutate<T>(T value) where T : IList<int> { }

                public static void Run()
                {
                    {{statement}}
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .Count()
            .IsEqualTo(reports);
    }
}
