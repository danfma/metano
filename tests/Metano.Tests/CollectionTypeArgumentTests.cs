using System.Text.RegularExpressions;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests;

/// <summary>
/// Spec 011 FR-008 and D31 (stage 011c, group J): the collections a
/// compilation asks a runtime question about carry their type arguments, a
/// collection test renders through the descriptor factories, and a collection
/// interface some user type implements is reported (W4-17) instead of
/// answering <c>false</c>.
/// </summary>
public class CollectionTypeArgumentTests
{
    private const string Header = """
        using System.Collections.Generic;
        using System.Linq;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Slots;

        public record Point(int X, int Y);

        """;

    internal const string GoldenSource =
        Header
        + """
            public class IntList : List<int>;

            public static class Probe
            {
                public static bool IsList(object o) => o is List<string>;

                public static bool IsReadOnly(object o) => o is IReadOnlyList<object>;

                public static List<int> Make() => new List<int> { 1, 2 };

                public static List<int> Empty() => new List<int>();

                public static Dictionary<string, Point> Points() =>
                    new Dictionary<string, Point> { ["a"] = new(1, 2) };

                public static List<int> Linq(IEnumerable<int> xs) => xs.ToList();

                public static Queue<int> Unasked() => new Queue<int>();
            }
            """;

    [Test]
    public async Task TypeArguments_MatchTheGolden() =>
        await TranspileHelper.AssertGolden(
            "collections-type-arguments.ts",
            RuntimeTypeDescriptorTests.TranspileAll(GoldenSource)
        );

    [Test]
    public async Task TheGoldenSource_ReportsNoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(GoldenSource);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task WithoutAQuestion_NoSlotAndNoReflectionImport()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static List<int> Make() => new List<int> { 1 };

                    public static List<int> Empty() => new List<int>();
                }
                """
        );

        await Assert.That(files["slots/probe.ts"]).Contains("List.of<number>([1])");
        await Assert.That(files["slots/probe.ts"]).Contains("new List<number>()");
        await Assert.That(files["slots/probe.ts"]).DoesNotContain("Int32Type");
    }

    [Test]
    public async Task ADispatcherOrCastDoesNotQuestion()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static List<int> Cast(object o) => (List<int>)o;

                    public static List<int> Empty() => new List<int>();
                }
                """
        );

        await Assert.That(files["slots/probe.ts"]).Contains("new List<number>()");
    }

    [Test]
    public async Task AClassArgument_IsReachedAndKeepsItsDescriptor()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;

                    public static List<Point> Points() => new List<Point>();
                }
                """
        );

        await Assert
            .That(files["slots/probe.ts"])
            .Contains("new List<Point>(null, [Point.$runtimeType])");

        await Assert.That(files["slots/point.ts"]).Contains("static get $runtimeType()");
    }

    [Test]
    public async Task AGenericSubclass_ClosesWithUnknownAndAConcreteOneWithItsDescriptor()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public class IntList : List<int>;

                public class Wrapped<T> : List<T>;

                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                }
                """
        );

        await Assert.That(files["slots/int-list.ts"]).Contains("super(null, [Int32Type]);");
        await Assert.That(files["slots/wrapped.ts"]).Contains("super(null, [UnknownType]);");
    }

    [Test]
    public async Task AUserImplementer_MakesAnInterfaceTestMs0001()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public class Mine : IEnumerable<int>, ICollection<int>
                {
                    public int Count => 0;
                    public bool IsReadOnly => false;
                    public void Add(int item) { }
                    public void Clear() { }
                    public bool Contains(int item) => false;
                    public void CopyTo(int[] array, int arrayIndex) { }
                    public bool Remove(int item) => false;
                    public IEnumerator<int> GetEnumerator() => throw new System.NotImplementedException();
                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
                        throw new System.NotImplementedException();
                }

                public static class Probe
                {
                    public static bool Holds(object o) => o is ICollection<int>;

                    public static bool Narrow(List<int> xs) => xs is ICollection<int>;

                    public static bool AClass(object o) => o is List<int>;
                }
                """
        );

        var unsupported = diagnostics.Where(d => d.Code == "MS0001").ToList();

        await Assert.That(unsupported.Count).IsEqualTo(1);
        await Assert.That(unsupported[0].Message).Contains("'o is ICollection<int>'");

        await Assert
            .That(files["slots/probe.ts"])
            .Contains("Unsupported expression: CollectionInterfaceTest");

        await Assert.That(files["slots/probe.ts"]).Contains("isType(o, listType(Int32Type))");
    }

    [Test]
    public async Task AnArgumentWithoutADescriptor_ReportsMs0046()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public static class Probe
                {
                    public static bool Is<T>(object o) => o is List<T>;
                }
                """
        );

        await Assert.That(diagnostics.Count(d => d.Code == "MS0046")).IsEqualTo(1);
    }

    [Test]
    public async Task TheCompilersImplementerTable_AgreesWithTheRuntimeClassLiterals()
    {
        var directory = Path.Combine(
            TranspileHelper.RepositoryRoot(),
            "targets",
            "js",
            "metano-runtime",
            "src",
            "system",
            "collections"
        );

        var classes = new Dictionary<IrCollectionKind, string>
        {
            [IrCollectionKind.List] = "list.ts",
            [IrCollectionKind.Dictionary] = "dictionary.ts",
            [IrCollectionKind.HashSet] = "hash-set.ts",
            [IrCollectionKind.Queue] = "queue.ts",
            [IrCollectionKind.Stack] = "stack.ts",
        };

        foreach (var (kind, file) in classes)
        {
            var text = File.ReadAllText(Path.Combine(directory, file));
            var literal = text[text.IndexOf("interfaces: [", StringComparison.Ordinal)..];
            literal = literal[..literal.IndexOf("test:", StringComparison.Ordinal)];

            var runtime = Regex
                .Matches(literal, "collection: \"(?<name>[A-Za-z]+)\"")
                .Select(match => match.Groups["name"].Value)
                .Order(StringComparer.Ordinal)
                .ToList();

            var compiler = RuntimeCollections
                .InterfacesImplementedBy(kind)
                .Select(RuntimeCollections.NameOf)
                .Order(StringComparer.Ordinal)
                .ToList();

            await Assert.That(compiler).IsEquivalentTo(runtime);
        }
    }
}
