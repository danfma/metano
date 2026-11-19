using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// The neutral collection facts of spec 011 stage 011c (data-model § 2): the
/// kind of each BCL collection, its pre-011c legacy form, the receiver type of
/// every collection element access, the static type of a loop's collection and
/// the collection a collection expression constructs.
/// </summary>
public class CollectionTypeFactsTests
{
    private static readonly IrPrimitiveTypeRef Int32Type = new(IrPrimitive.Int32);

    private static readonly IrPrimitiveTypeRef StringType = new(IrPrimitive.String);

    [Test]
    [Arguments("List<int>", IrCollectionKind.List)]
    [Arguments("IList<int>", IrCollectionKind.IList)]
    [Arguments("ICollection<int>", IrCollectionKind.ICollection)]
    [Arguments("IReadOnlyList<int>", IrCollectionKind.IReadOnlyList)]
    [Arguments("IReadOnlyCollection<int>", IrCollectionKind.IReadOnlyCollection)]
    [Arguments("Dictionary<string, int>", IrCollectionKind.Dictionary)]
    [Arguments("IDictionary<string, int>", IrCollectionKind.IDictionary)]
    [Arguments("IReadOnlyDictionary<string, int>", IrCollectionKind.IReadOnlyDictionary)]
    [Arguments("HashSet<int>", IrCollectionKind.HashSet)]
    [Arguments("ISet<int>", IrCollectionKind.ISet)]
    [Arguments("Queue<int>", IrCollectionKind.Queue)]
    [Arguments("Stack<int>", IrCollectionKind.Stack)]
    [Arguments("Dictionary<string, int>.KeyCollection", IrCollectionKind.KeyCollection)]
    [Arguments("Dictionary<string, int>.ValueCollection", IrCollectionKind.ValueCollection)]
    public async Task StageCollection_ResolvesToItsKind(string type, IrCollectionKind expected)
    {
        var mapped = MapFieldType(type);

        await Assert.That(mapped).IsTypeOf<IrCollectionTypeRef>();
        await Assert.That(((IrCollectionTypeRef)mapped).Kind).IsEqualTo(expected);
    }

    [Test]
    [Arguments("IReadOnlySet<int>")]
    [Arguments("SortedSet<int>")]
    [Arguments("SortedDictionary<string, int>")]
    [Arguments("System.Collections.Concurrent.ConcurrentDictionary<string, int>")]
    [Arguments("LinkedList<int>")]
    [Arguments("IEnumerable<int>")]
    [Arguments("IntList")]
    [Arguments("List<int>.Enumerator")]
    public async Task OtherType_HasNoStageKind(string type)
    {
        var mapped = MapFieldType(type, "public class IntList : List<int> { }");

        await Assert.That(mapped).IsNotTypeOf<IrCollectionTypeRef>();
    }

    [Test]
    public async Task DictionaryView_CarriesTheViewedElement()
    {
        var keys = (IrCollectionTypeRef)MapFieldType("Dictionary<string, int>.KeyCollection");
        var values = (IrCollectionTypeRef)MapFieldType("Dictionary<string, int>.ValueCollection");

        await Assert.That(keys.TypeArguments.Single()).IsEqualTo(StringType);
        await Assert.That(values.TypeArguments.Single()).IsEqualTo(Int32Type);
    }

    [Test]
    [Arguments("IReadOnlyList<int>", IrVariance.Covariant)]
    [Arguments("IReadOnlyCollection<int>", IrVariance.Covariant)]
    [Arguments("IList<int>", IrVariance.Invariant)]
    [Arguments("List<int>", IrVariance.Invariant)]
    public async Task Variance_FollowsTheDefinition(string type, IrVariance expected)
    {
        var mapped = (IrCollectionTypeRef)MapFieldType(type);

        await Assert.That(mapped.Variance.Single()).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadOnlyDictionary_IsInvariant()
    {
        var mapped = (IrCollectionTypeRef)MapFieldType("IReadOnlyDictionary<string, int>");

        await Assert
            .That(mapped.Variance)
            .IsEquivalentTo([IrVariance.Invariant, IrVariance.Invariant]);
    }

    [Test]
    [Arguments("List<int>")]
    [Arguments("IList<int>")]
    [Arguments("ICollection<int>")]
    [Arguments("IReadOnlyList<int>")]
    [Arguments("Queue<int>")]
    [Arguments("Stack<int>")]
    [Arguments("Dictionary<string, int>.ValueCollection")]
    public async Task ListShapedKind_HasAnArrayLegacyForm(string type)
    {
        var legacy = ((IrCollectionTypeRef)MapFieldType(type)).LegacyForm;

        await Assert.That(legacy).IsEqualTo(new IrArrayTypeRef(Int32Type));
        await Assert.That(((IrArrayTypeRef)legacy).IsNative).IsFalse();
    }

    [Test]
    public async Task ReadOnlyCollection_HasAnIterableLegacyForm()
    {
        var legacy = ((IrCollectionTypeRef)MapFieldType("IReadOnlyCollection<int>")).LegacyForm;

        await Assert.That(legacy).IsEqualTo(new IrIterableTypeRef(Int32Type));
    }

    [Test]
    [Arguments("Dictionary<string, int>")]
    [Arguments("IDictionary<string, int>")]
    [Arguments("IReadOnlyDictionary<string, int>")]
    public async Task DictionaryKind_HasAMapLegacyForm(string type)
    {
        var legacy = ((IrCollectionTypeRef)MapFieldType(type)).LegacyForm;

        await Assert.That(legacy).IsEqualTo(new IrMapTypeRef(StringType, Int32Type));
    }

    [Test]
    [Arguments("HashSet<int>")]
    [Arguments("ISet<int>")]
    public async Task SetKind_HasASetLegacyForm(string type)
    {
        var legacy = ((IrCollectionTypeRef)MapFieldType(type)).LegacyForm;

        await Assert.That(legacy).IsEqualTo(new IrSetTypeRef(Int32Type));
    }

    [Test]
    public async Task Equality_OfTwoExtractions_ComparesByValue()
    {
        var first = MapFieldType("List<List<int>>");
        var second = MapFieldType("List<List<int>>");

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    [Arguments("IList<List<int>>")]
    [Arguments("IReadOnlyList<IList<int>>")]
    public async Task Equality_FollowsTheLegacyForm(string sameLegacyShape)
    {
        var list = MapFieldType("List<List<int>>");
        var other = MapFieldType(sameLegacyShape);

        await Assert.That(list).IsEqualTo(other);
    }

    [Test]
    public async Task Equality_TellsLegacyShapesApart()
    {
        var list = MapFieldType("List<int>");
        var set = MapFieldType("HashSet<int>");

        await Assert.That(list).IsNotEqualTo(set);
    }

    [Test]
    [Arguments("List<int>", "int[]")]
    [Arguments("Dictionary<string, int>", "IDictionary<string, int>")]
    [Arguments("List<List<int>>", "int[][]")]
    public async Task Equivalence_FollowsTheLegacyForm(string collection, string sameLegacyShape)
    {
        var left = MapFieldType(collection);
        var right = MapFieldType(sameLegacyShape);

        await Assert.That(IrTypeRefEquivalence.AreEquivalent(left, right)).IsTrue();
        await Assert.That(IrTypeRefEquivalence.AreEquivalent(right, left)).IsTrue();
    }

    [Test]
    public async Task Equivalence_TellsLegacyShapesApart()
    {
        var list = MapFieldType("List<int>");
        var set = MapFieldType("HashSet<int>");

        await Assert.That(IrTypeRefEquivalence.AreEquivalent(list, set)).IsFalse();
    }

    [Test]
    public async Task DeepLegacyForm_ReplacesEveryNestedCollection()
    {
        var nested = MapFieldType("Dictionary<string, List<List<int>>>").ToLegacyTypeRef();
        var arrays = MapFieldType("Dictionary<string, int[][]>").ToLegacyTypeRef();

        await Assert.That(nested).IsEqualTo(arrays);
    }

    [Test]
    public async Task DeepLegacyForm_KeepsACollectionFreeRef()
    {
        var array = MapFieldType("int[][]");

        await Assert.That(ReferenceEquals(array.ToLegacyTypeRef(), array)).IsTrue();
    }

    [Test]
    [Arguments("IReadOnlySet<int>")]
    [Arguments("Dictionary<string, IReadOnlySet<int>>")]
    public async Task ReadOnlySet_IsNeverResolvedBeforeTheFlip(string type)
    {
        // The deep legacy form reads LegacyForm on every collection ref, and
        // LegacyForm throws for the IReadOnlySet kind: no throw means the
        // resolver produced none at any nesting level.
        var mapped = MapFieldType(type);

        await Assert.That(() => mapped.ToLegacyTypeRef()).ThrowsNothing();
    }

    [Test]
    [Arguments("xs[0] = 1;")]
    [Arguments("xs[0] += 1;")]
    [Arguments("xs[0]++;")]
    [Arguments("var x = xs[0];")]
    [Arguments("var x = xs?[0];")]
    [Arguments("holder?.Items[0] = 1;")]
    [Arguments("holder?.Items[0] += 1;")]
    [Arguments("map[\"a\"] = 1;")]
    [Arguments("map[\"a\"] = map[\"a\"] + 1;")]
    [Arguments("holder?.Map[\"a\"] = 1;")]
    [Arguments("var d = new Dictionary<string, int> { [\"a\"] = 1 };")]
    public async Task CollectionElementAccess_CarriesItsReceiverType(string statement)
    {
        var body = ExtractBody(statement);
        var accesses = Descendants(body).OfType<IrElementAccess>().ToList();

        await Assert.That(accesses).IsNotEmpty();

        foreach (var access in accesses)
            await Assert.That(access.TargetType).IsTypeOf<IrCollectionTypeRef>();
    }

    [Test]
    public async Task ForEach_CarriesTheCollectionType()
    {
        var body = ExtractBody("foreach (var x in xs) { }");
        var loop = body.OfType<IrForEachStatement>().Single();

        await Assert.That(loop.CollectionType).IsTypeOf<IrCollectionTypeRef>();

        await Assert
            .That(((IrCollectionTypeRef)loop.CollectionType!).Kind)
            .IsEqualTo(IrCollectionKind.List);
    }

    [Test]
    public async Task ForEach_OverAnArray_HasNoCollectionType()
    {
        var body = ExtractBody("foreach (var x in array) { }");
        var loop = body.OfType<IrForEachStatement>().Single();

        await Assert.That(loop.CollectionType).IsTypeOf<IrArrayTypeRef>();
    }

    [Test]
    public async Task TupleDeconstructionIntoElements_IsUnsupported()
    {
        // Deconstruction into existing locations is not modelled (spec 003,
        // research D7), so the swap holds no element access to carry a type.
        var body = ExtractBody("(xs[0], xs[1]) = (xs[1], xs[0]);");
        var expressions = Descendants(body).ToList();

        await Assert.That(expressions.OfType<IrUnsupportedExpression>()).IsNotEmpty();
        await Assert.That(expressions.OfType<IrElementAccess>()).IsEmpty();
    }

    [Test]
    [Arguments("List<int>", IrCollectionKind.List)]
    [Arguments("IList<int>", IrCollectionKind.List)]
    [Arguments("ICollection<int>", IrCollectionKind.List)]
    [Arguments("IReadOnlyList<int>", IrCollectionKind.IReadOnlyList)]
    [Arguments("IReadOnlyCollection<int>", IrCollectionKind.IReadOnlyCollection)]
    public async Task CollectionExpression_CarriesTheCollectionItBuilds(
        string type,
        IrCollectionKind expected
    )
    {
        var literal = Descendants(ExtractBody($"{type} c = [1, 2];"))
            .OfType<IrArrayLiteral>()
            .Single();

        await Assert.That(literal.TargetCollection?.Kind).IsEqualTo(expected);
    }

    [Test]
    [Arguments("int[]")]
    [Arguments("IEnumerable<int>")]
    public async Task CollectionExpression_OfAnArrayTarget_BuildsNoCollection(string type)
    {
        var literal = Descendants(ExtractBody($"{type} c = [1, 2];"))
            .OfType<IrArrayLiteral>()
            .Single();

        await Assert.That(literal.TargetCollection).IsNull();
    }

    private static IrTypeRef MapFieldType(string type, string extraTypes = "")
    {
        var compilation = IrTestHelper.Compile(
            $$"""
            public class Holder { public {{type}} Field = default!; }
            {{extraTypes}}
            """
        );

        var field = compilation
            .GetTypeByMetadataName("Holder")!
            .GetMembers("Field")
            .OfType<IFieldSymbol>()
            .Single();

        return IrTypeRefMapper.Map(field.Type);
    }

    private static IReadOnlyList<IrStatement> ExtractBody(string statement)
    {
        var compilation = IrTestHelper.Compile(
            $$"""
            public class Holder
            {
                public List<int> Items = new();
                public Dictionary<string, int> Map = new();
            }
            public class T
            {
                void Run(List<int> xs, Dictionary<string, int> map, Holder? holder, int[] array)
                {
                    {{statement}}
                }
            }
            """
        );

        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var extractor = new IrStatementExtractor(model, IrExtractionContext.Empty);

        return extractor.ExtractBody(method.Body, method.ExpressionBody, isVoid: true).Statements;
    }

    private static IEnumerable<IrExpression> Descendants(IEnumerable<IrStatement> statements) =>
        statements.SelectMany(Descendants);

    private static IEnumerable<IrExpression> Descendants(IrStatement statement) =>
        IrNodeChildren
            .ExpressionsOf(statement)
            .SelectMany(Descendants)
            .Concat(IrNodeChildren.StatementsOf(statement).SelectMany(Descendants));

    private static IEnumerable<IrExpression> Descendants(IrExpression expression) =>
        IrNodeChildren
            .ExpressionsOf(expression)
            .SelectMany(Descendants)
            .Concat(IrNodeChildren.StatementsOf(expression).SelectMany(Descendants))
            .Prepend(expression);
}
