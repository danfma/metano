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

                public static bool IsReadOnly(List<object> o) => o is IReadOnlyList<string>;

                public static IReadOnlyList<object> Covariant(string[] xs) => xs;

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
    public async Task AReadOnlyLiteralAndArrayConversion_CloseTheEmittedList()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(object o) => o is IReadOnlyList<string>;
                    public static IReadOnlyList<int> Literal() => [1];
                    public static IList<int> Convert(int[] xs) => xs;
                }
                """
        );

        await Assert.That(files["slots/probe.ts"]).Contains("[Int32Type]");
        await Assert.That(files["slots/probe.ts"].Split("[Int32Type]").Length).IsEqualTo(3);
    }

    [Test]
    public async Task ADispatchedSubclass_PassesSlotsOnEverySuperPath()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public class Bag : List<int>
                {
                    public Bag() : this(new int[] { 1 }) { }
                    public Bag(IEnumerable<int> xs) : base(xs) { }
                }
                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                }
                """
        );

        await Assert.That(files["slots/bag.ts"]).Contains("super(xs, [Int32Type]);");
        await Assert.That(files["slots/bag.ts"]).DoesNotContain("super(xs);");
    }

    [Test]
    public async Task ADirectArrayInterfaceTest_ReportsRatherThanReturningFalse()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(int[] xs) => xs is IList<int>;
                    public static bool Narrow(List<int> xs) => xs is IList<int>;
                }
                """
        );

        await Assert.That(diagnostics.Count(d => d.Code == "MS0001")).IsEqualTo(1);

        await Assert
            .That(files["slots/probe.ts"])
            .Contains("Unsupported expression: CollectionInterfaceTest");

        await Assert.That(files["slots/probe.ts"]).Contains("isType(xs, iListType(Int32Type))");
    }

    [Test]
    public async Task AnArrayBehindAnOperandThatCanHoldOne_ReportsMs0001()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public static class Probe
                {
                    public static bool ObjectList(object o) => o is IList<int>;
                    public static bool ObjectReadOnly(object o) => o is IReadOnlyList<int>;
                    public static bool ObjectCollection(object o) => o is ICollection<int>;
                    public static bool ObjectReadOnlyCollection(object o) => o is IReadOnlyCollection<int>;
                    public static bool Enumerable(IEnumerable<int> e) => e is IList<int>;
                    public static bool EnumerableReadOnly(IEnumerable<int> e) => e is IReadOnlyList<int>;
                    public static bool Untyped(System.Collections.IEnumerable e) => e is IList<int>;
                    public static bool Wide(System.Array a) => a is IList<int>;
                    public static bool Narrower(ICollection<int> c) => c is IList<int>;
                    public static bool Generic<T>(T value) => value is IList<int>;
                    public static IList<int>? As(object o) => o as IList<int>;
                    public static string Switch(object o) => o switch { IReadOnlyList<int> => "l", _ => "?" };
                }
                """
        );

        await Assert.That(diagnostics.Count(d => d.Code == "MS0001")).IsEqualTo(12);

        await Assert
            .That(
                files["slots/probe.ts"]
                    .Split("Unsupported expression: CollectionInterfaceTest")
                    .Length
            )
            .IsEqualTo(13);
    }

    [Test]
    public async Task AClassTestOrAnOperandThatCannotHoldAnArray_ReportsNothing()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public static class Probe
                {
                    public static bool Class(object o) => o is List<int>;
                    public static bool Dictionary(object o) => o is Dictionary<string, int>;
                    public static bool Set(object o) => o is HashSet<int>;
                    public static bool DictionaryInterface(object o) => o is IDictionary<string, int>;
                    public static bool SetInterface(object o) => o is ISet<int>;
                    public static bool Enumerable(IEnumerable<int> e) => e is List<int>;
                    public static bool OfAList(List<object> xs) => xs is IReadOnlyList<string>;
                    public static bool OfADictionary(Dictionary<string, int> d) => d is IDictionary<string, int>;
                }
                """
        );

        await Assert.That(diagnostics.Count(d => d.Code == "MS0001")).IsEqualTo(0);

        await Assert
            .That(files["slots/probe.ts"])
            .DoesNotContain("Unsupported expression: CollectionInterfaceTest");

        await Assert.That(files["slots/probe.ts"]).Contains("isType(o, listType(Int32Type))");
    }

    [Test]
    public async Task ADictionaryViewBehindACollectionOperand_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public static class Probe
                {
                    public static bool Object(object keys) => keys is IReadOnlyCollection<string>;
                    public static bool Enumerable(IEnumerable<string> keys) => keys is ICollection<string>;
                    public static bool OfADictionary(Dictionary<string, int> d) =>
                        d is IReadOnlyCollection<KeyValuePair<string, int>>;
                }
                """
        );

        var unsupported = diagnostics.Where(d => d.Code == "MS0001").ToList();

        await Assert.That(unsupported.Count).IsEqualTo(2);
        await Assert.That(unsupported[0].Message).Contains("a dictionary view");
    }

    [Test]
    public async Task AnArrayConversion_TakesTheSlotFromTheSourceElement()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                    public static IReadOnlyList<object> Covariant(string[] xs) => xs;
                    public static IList<object> Mutable(string[] xs) => xs;
                    public static IReadOnlyList<object> Literal() => ["a"];
                }
                """
        );

        var text = files["slots/probe.ts"];

        await Assert
            .That(text)
            .Contains("List.adopt<Object>(xs, { fixedSize: true }, [StringType])");

        await Assert.That(text.Split("[StringType]").Length).IsEqualTo(3);
        await Assert.That(text).Contains("[ObjectType]");
    }

    [Test]
    public async Task AnArrayConversion_ReachesTheSourceElementDescriptor()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                    public static IReadOnlyList<object> Covariant(Point[] xs) => xs;
                }
                """
        );

        await Assert.That(files["slots/probe.ts"]).Contains("[Point.$runtimeType]");
        await Assert.That(files["slots/point.ts"]).Contains("static get $runtimeType()");
    }

    [Test]
    public async Task ADirectDictionaryView_ReportsMs0001InEveryTestForm()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(Dictionary<string, int> d) => d.Keys is IReadOnlyCollection<string>;
                    public static bool Collection(Dictionary<string, int>.KeyCollection keys) => keys is ICollection<string>;
                    public static bool Values(Dictionary<string, int> d) => d.Values is IReadOnlyCollection<int>;
                    public static bool Wide(Dictionary<string, int>.KeyCollection keys) => keys is IReadOnlyCollection<object>;
                    public static bool Nullable(Dictionary<string, int>.KeyCollection? keys) => keys is IReadOnlyCollection<string>;
                    public static object? As(Dictionary<string, int>.KeyCollection keys) => keys as IReadOnlyCollection<string>;
                    public static string Switch(Dictionary<string, int>.KeyCollection keys) =>
                        keys switch { IReadOnlyCollection<string> => "c", _ => "?" };
                }
                """
        );

        await Assert.That(diagnostics.Count(d => d.Code == "MS0001")).IsEqualTo(7);

        await Assert
            .That(files["slots/probe.ts"])
            .DoesNotContain("iReadOnlyCollectionType(StringType)");
    }

    [Test]
    public async Task AnExplicitArrayCast_ReachesTheSourceElementDescriptor()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public class DerivedOnlyInCast;

                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                    public static IReadOnlyList<object> Cast(DerivedOnlyInCast[] xs) => (IReadOnlyList<object>)xs;
                    public static IList<object> Mutable(DerivedOnlyInCast[] xs) => (IList<object>)xs;
                }
                """
        );

        await Assert.That(files["slots/probe.ts"]).Contains("[DerivedOnlyInCast.$runtimeType]");
        await Assert.That(files["slots/probe.ts"]).DoesNotContain("UnknownType");
    }

    private const string JsonHeader = """
        public enum Color { Red = 1, Blue = 2 }

        public sealed class BagDto
        {
            public List<Color> Colors { get; set; } = new();
            public IReadOnlyList<int> ReadOnly { get; set; } = new List<int>();
            public IList<int> Mutable { get; set; } = new List<int>();
            public IDictionary<string, int> Map { get; set; } = new Dictionary<string, int>();
            public ISet<string> Set { get; set; } = new HashSet<string>();
        }

        [System.Text.Json.Serialization.JsonSerializable(typeof(BagDto))]
        public partial class BagJsonContext : System.Text.Json.Serialization.JsonSerializerContext
        {
            public BagJsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
        }

        public static class Probe
        {
            public static bool Is(object o) => o is Dictionary<string, string>;
            public static bool IsList(object o) => o is List<string>;
            public static bool IsSet(object o) => o is HashSet<string>;
        }
        """;

    [Test]
    public async Task InterfaceTypedJsonProperties_CarryTheSlotOfTheirConcreteClass()
    {
        var files = TranspileHelper.Transpile(Header + JsonHeader);
        var context = files["slots/bag-json-context.ts"];

        await Assert.That(context).Contains("List.of(items, [Int32Type])");

        await Assert
            .That(context)
            .Contains("Dictionary.assign(entries, null, [StringType, Int32Type])");

        await Assert.That(context).Contains("HashSet.from(items, null, [StringType])");
        await Assert.That(context).DoesNotContain("List.of(items)");
    }

    [Test]
    public async Task AJsonOnlyElementType_IsReachedAndKeepsItsDescriptor()
    {
        var files = TranspileHelper.Transpile(Header + JsonHeader);

        await Assert.That(files["slots/bag-json-context.ts"]).Contains("[Color$runtimeType]");
        await Assert.That(files["slots/color.ts"]).Contains("Color$runtimeType");
        await Assert.That(files["slots/bag-json-context.ts"]).DoesNotContain("UnknownType");
    }

    [Test]
    public async Task ReferenceNullability_IsNotANullableValueDescriptor()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                    public static List<string?> Strings() => new();
                    public static List<Point?> Points() => new();
                    public static List<int?> Values() => new();
                }
                """
        );

        await Assert.That(files["slots/probe.ts"]).Contains("[StringType]");
        await Assert.That(files["slots/probe.ts"]).Contains("[Point.$runtimeType]");
        await Assert.That(files["slots/probe.ts"]).Contains("[nullableType(Int32Type)]");
        await Assert.That(files["slots/probe.ts"]).DoesNotContain("nullableType(StringType)");
    }

    [Test]
    public async Task ASubclassReachedOnlyThroughACollectionArgument_OverridesTheRuntimeGetter()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public class Bag : List<int>;
                public static class Probe
                {
                    public static bool Is(object o) => o is List<string>;
                    public static List<Bag> Make() => new();
                }
                """
        );

        await Assert.That(files["slots/bag.ts"]).Contains("static override get $runtimeType()");
        await Assert.That(files["slots/bag.ts"]).DoesNotContain("base: () => List.$runtimeType");
    }

    [Test]
    public async Task ASubclassReachedOnlyThroughAJsonMember_OverridesTheRuntimeGetter()
    {
        var files = TranspileHelper.Transpile(
            Header
                + """
                public class Bag : List<int>;
                public record Hub(Bag Items);
                [System.Text.Json.Serialization.JsonSerializable(typeof(Hub))]
                public partial class JsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
                """
        );

        await Assert.That(files["slots/bag.ts"]).Contains("static override get $runtimeType()");
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
