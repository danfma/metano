using System.Text.RegularExpressions;

namespace Metano.Tests;

public class JsonSerializerContextTests
{
    [Test]
    public async Task BasicContext_GeneratesSerializerContextClass()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record TodoItem(string Title, bool Completed);

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(TodoItem))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        await Assert.That(result).ContainsKey("test-app/json-context.ts");
        var ts = result["test-app/json-context.ts"];

        // Should extend SerializerContext
        await Assert.That(ts).Contains("extends SerializerContext");

        // Should have static _default field and static getter
        await Assert.That(ts).Contains("private static readonly _default");
        await Assert.That(ts).Contains("static get default()");

        // Should have lazy getter for todoItem
        await Assert.That(ts).Contains("get todoItem()");
        await Assert.That(ts).Contains("TypeSpec<TodoItem>");
        await Assert.That(ts).Contains("this.createSpec(");

        // Should have pre-computed JSON names (default = PascalCase since no policy)
        await Assert.That(ts).Contains("\"Title\"");
        await Assert.That(ts).Contains("\"Completed\"");

        // Should have TS field names in camelCase
        await Assert.That(ts).Contains("ts: \"title\"");
        await Assert.That(ts).Contains("ts: \"completed\"");
    }

    [Test]
    public async Task CamelCasePolicy_PreComputesJsonNames()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record UserProfile(string FirstName, string LastName, int Age);

            [Transpile]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
            [JsonSerializable(typeof(UserProfile))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        // CamelCase policy: FirstName → firstName, LastName → lastName
        await Assert.That(ts).Contains("json: \"firstName\"");
        await Assert.That(ts).Contains("json: \"lastName\"");
        await Assert.That(ts).Contains("json: \"age\"");
    }

    [Test]
    public async Task SnakeCasePolicy_PreComputesJsonNames()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record OrderItem(string ProductName, decimal UnitPrice);

            [Transpile]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
            [JsonSerializable(typeof(OrderItem))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        // SnakeCaseLower: ProductName → product_name, UnitPrice → unit_price
        await Assert.That(ts).Contains("json: \"product_name\"");
        await Assert.That(ts).Contains("json: \"unit_price\"");
    }

    [Test]
    public async Task JsonPropertyName_OverridesNamingPolicy()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Item(
                [property: JsonPropertyName("item_id")] string Id,
                string Name
            );

            [Transpile]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
            [JsonSerializable(typeof(Item))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        // JsonPropertyName wins over policy
        await Assert.That(ts).Contains("json: \"item_id\"");
        // Policy applies to Name
        await Assert.That(ts).Contains("json: \"name\"");
    }

    [Test]
    public async Task JsonIgnore_ExcludesProperty()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Secret(
                string Name,
                [property: JsonIgnore] string InternalToken
            );

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(Secret))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        await Assert.That(ts).Contains("ts: \"name\"");
        await Assert.That(ts).DoesNotContain("ts: \"internalToken\"");
        await Assert.That(ts).DoesNotContain("InternalToken");
        // The factory still fills every constructor slot so the call
        // matches the emitted constructor's arity; the ignored member is
        // absent from the payload and arrives as undefined.
        await Assert.That(ts).Contains("new Secret(p.name as string, p.internalToken as string)");
    }

    [Test]
    public async Task NullableProperty_SetsOptionalFlag()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Note(string Title, string? Body);

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(Note))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        // Body should be nullable with optional flag
        await Assert.That(ts).Contains("kind: \"nullable\"");
        await Assert.That(ts).Contains("optional: true");
    }

    [Test]
    public async Task MultipleSerializableTypes_GeneratesMultipleGetters()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Person(string Name, int Age);

            [Transpile]
            public record Address(string Street, string City);

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(Person))]
            [JsonSerializable(typeof(Address))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        await Assert.That(ts).Contains("get person()");
        await Assert.That(ts).Contains("get address()");
        await Assert.That(ts).Contains("TypeSpec<Person>");
        await Assert.That(ts).Contains("TypeSpec<Address>");
    }

    [Test]
    public async Task StringEnumProperty_ClassifiedAsEnum()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile, StringEnum]
            public enum Status { Draft, Active, Completed }

            [Transpile]
            public record Task(string Title, Status CurrentStatus);

            [Transpile]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
            [JsonSerializable(typeof(Task))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        await Assert.That(ts).Contains("kind: \"enum\"");
        await Assert.That(ts).Contains("values: Status");
    }

    [Test]
    public async Task ArrayProperty_ClassifiedAsArray()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Container(string Name, List<string> Tags);

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(Container))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        await Assert.That(ts).Contains("kind: \"array\"");
        await Assert.That(ts).Contains("kind: \"primitive\"");
    }

    [Test]
    public async Task GuidProperty_ClassifiedAsBrandedWithUuidCreate()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Entity(Guid Id, string Name);

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(Entity))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];

        // Guid should be classified as a branded descriptor pointing at UUID.create
        await Assert.That(ts).Contains("kind: \"branded\"");
        await Assert.That(ts).Contains("create: UUID.create");
        // And UUID should be imported from metano-runtime
        await Assert.That(ts).Contains("import { UUID } from \"metano-runtime\"");
    }

    [Test]
    public async Task IEnumerableProperty_ClassifiesAsArrayDescriptor()
    {
        // After #129, `IEnumerable<T>` lowers to `Iterable<T>` at the
        // type level, but JSON-wise it still serializes as an array.
        // The serializer context must classify the property as
        // `kind: "array"` so the runtime knows to iterate and emit
        // each element through the inner descriptor — falling
        // through to `kind: "ref"` would break serialization.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Tag(string Value);

            [Transpile]
            public record Article(string Title, IEnumerable<Tag> Tags);

            [Transpile]
            [JsonSerializable(typeof(Article))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        var ts = result["test-app/json-context.ts"];
        await Assert.That(ts).Contains("kind: \"array\"");
    }

    private const string ClipContextSource = """
        using System.Text.Json.Serialization;

        namespace TestApp;

        [Transpile]
        public abstract record Clip(double Seconds);

        [Transpile]
        public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

        [Transpile]
        [JsonSourceGenerationOptions]
        [JsonSerializable(typeof(Clip))]
        [JsonSerializable(typeof(ImageClip))]
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task AbstractType_FactoryThrowsInsteadOfConstructing()
    {
        var ts = TranspileHelper.Transpile(ClipContextSource)["test-app/json-context.ts"];

        await Assert.That(ts).DoesNotContain("new Clip(");

        // A throwing factory never reads the payload, so its arrow declares
        // no parameter: an unused `p` trips biome's noUnusedFunctionParameters
        // in every consumer project (#480).
        await Assert.That(ts).DoesNotContain("factory: (p: Record<string, unknown>) => {");
        await Assert.That(ts).Contains("factory: () => {");

        // The reason is the abstractness itself, not polymorphism (#489);
        // System.Text.Json rejects the type the same way.
        await Assert
            .That(ts)
            .Contains("throw new Error(\"Cannot construct abstract type 'Clip'.\");");
    }

    [Test]
    public async Task DerivedType_FactoryPassesForwardedParamInConstructorOrder()
    {
        var ts = TranspileHelper.Transpile(ClipContextSource)["test-app/json-context.ts"];

        // `Seconds` is inherited but still a constructor slot, after `Path`.
        await Assert.That(ts).Contains("new ImageClip(p.path as string, p.seconds as number)");
    }

    [Test]
    public async Task ParameterlessConstructor_FactoryDeclaresNoPayloadParameter()
    {
        var ts = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Counter
            {
                public int Count { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Counter))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        )["test-app/json-context.ts"];

        // The factory binds no constructor slot and assigns nothing eagerly,
        // so the payload parameter would go unused (#480).
        await Assert.That(ts).Contains("factory: () => new Counter()");
    }

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    private static string TranspileContext(string declarations) =>
        TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{ContextBoilerplate}}
            """
        )["test-app/json-context.ts"];

    [Test]
    public async Task ArrayElementOnlyType_GetsItsOwnSpecGetter()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record ScriptWord(string Text, double Start);

            [Transpile]
            public sealed record Script(ScriptWord[] Words);

            [Transpile]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
            [JsonSerializable(typeof(Script))]
            """
        );

        await Assert.That(ts).IsEqualTo(TranspileHelper.ReadExpected("json-context-closure.ts"));
    }

    [Test]
    public async Task NestedRecordsAndCollections_EmitEveryReachableSpecOnce()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public sealed record Tagged(string Tag);

            [Transpile]
            public sealed record Keyed(int Id);

            [Transpile]
            public sealed record Middle(Leaf Leaf, Leaf? Spare, HashSet<Tagged> Tags);

            [Transpile]
            public sealed record Root(
                Middle Middle,
                Dictionary<string, Keyed> ById,
                List<Leaf?> Leaves
            );

            [Transpile]
            [JsonSerializable(typeof(Root))]
            """
        );

        string[] getters = ["root", "middle", "keyed", "leaf", "tagged"];
        var positions = getters.Select(x => ts.IndexOf($"  get {x}(): TypeSpec<")).ToList();

        await Assert.That(positions).DoesNotContain(-1);
        // Roots first, then breadth-first discovery order.
        await Assert
            .That(string.Join(",", positions))
            .IsEqualTo(string.Join(",", positions.Order()));

        foreach (var getter in getters)
            await Assert.That(CountGetters(ts, getter)).IsEqualTo(1);
    }

    [Test]
    public async Task CyclicReferences_TerminateWithOneGetterPerType()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Node(string Name, List<Edge> Edges);

            [Transpile]
            public sealed record Edge(double Weight, Node? Target);

            [Transpile]
            [JsonSerializable(typeof(Node))]
            """
        );

        await Assert.That(CountGetters(ts, "node")).IsEqualTo(1);
        await Assert.That(CountGetters(ts, "edge")).IsEqualTo(1);
        await Assert.That(ts).Contains("spec: () => this.edge");
        await Assert.That(ts).Contains("spec: () => this.node");
    }

    [Test]
    public async Task ReachedDerivedType_PullsInItsBaseSpec()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [Transpile]
            public sealed record Timeline(List<ImageClip> Clips);

            [Transpile]
            [JsonSerializable(typeof(Timeline))]
            """
        );

        await Assert.That(ts).Contains("get imageClip(): TypeSpec<ImageClip>");
        await Assert.That(ts).Contains("base: this.clip");
        await Assert.That(ts).Contains("get clip(): TypeSpec<Clip>");
    }

    // Under [assembly: TranspileAssembly] the base is emitted without
    // [Transpile], so its spec must still be linked — otherwise the inherited
    // property is neither written nor read and the factory reads an absent
    // `p.seconds` (#253).
    [Test]
    public async Task AssemblyWideTranspile_LinksTheBaseSpec()
    {
        var ts = TranspileHelper.Transpile(
            $$"""
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]

            namespace TestApp;

            public abstract record Clip(double Seconds);

            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [JsonSerializable(typeof(ImageClip))]
            {{ContextBoilerplate}}
            """
        )["test-app/json-context.ts"];

        await Assert.That(ts).Contains("base: this.clip");
        await Assert.That(ts).Contains("get clip(): TypeSpec<Clip>");
        await Assert.That(ts).Contains("ts: \"seconds\"");
        await Assert.That(ts).Contains("new ImageClip(p.path as string, p.seconds as number)");
    }

    // A base that emits no spec ([Ignore]) cannot be linked, so its
    // properties are inlined into the derived spec (#253), after the
    // derived type's own ones as System.Text.Json writes them (FR-009).
    [Test]
    public async Task UnlinkedBase_InlinesInheritedProperties()
    {
        var ts = TranspileContext(
            """
            [Ignore]
            public abstract record Media(string Id);

            [Transpile]
            public sealed record Photo(string Id, string Path) : Media(Id);

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert.That(ts).DoesNotContain("base: this.media");
        await Assert.That(ts).DoesNotContain("get media()");

        var idIndex = ts.IndexOf("ts: \"id\"", StringComparison.Ordinal);
        var pathIndex = ts.IndexOf("ts: \"path\"", StringComparison.Ordinal);

        await Assert.That(pathIndex).IsGreaterThan(-1);
        await Assert.That(idIndex).IsGreaterThan(pathIndex);
        await Assert.That(ts).Contains("new Photo(p.id as string, p.path as string)");
    }

    // System.Text.Json writes the most-derived type's own properties first,
    // then each base's, with an override at its most-derived position
    // (measured on .NET 10: {"Y":3,"Name":"d","X":2,"Base":1}). The runtime
    // has no hierarchy information for an inlined list, so the generator
    // emits it in that order (DECIDED-19 f).
    [Test]
    public async Task InlinedPropertyList_FollowsTheWireOrder()
    {
        var ts = TranspileContext(
            """
            public class Root
            {
                public int Base { get; init; }
                public virtual string Name { get; init; } = "";
            }

            public class Derived : Root
            {
                public int X { get; init; }
                public override string Name { get; init; } = "";
            }

            [Transpile]
            public sealed class Leaf : Derived
            {
                public int Y { get; init; }
                public override string Name { get; init; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Leaf))]
            """
        );

        var leafSpec = SpecBodyOf(ts, "leaf");

        await Assert.That(string.Join(",", JsonNamesOf(leafSpec))).IsEqualTo("Y,Name,X,Base");
    }

    // A [JsonIgnore] override over a linked base marks the base declaration
    // ignored, so the runtime neither writes nor reads it for the derived
    // contract (DECIDED-19 e); the base spec keeps it for its own instances.
    [Test]
    public async Task IgnoredOverrideOfLinkedBase_ListsTheIgnoredProperty()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class Media
            {
                public virtual string Name { get; init; } = "";
                public int Size { get; init; }
            }

            [Transpile]
            public sealed class Photo : Media
            {
                [JsonIgnore]
                public override string Name { get; init; } = "photo";
                public string Path { get; init; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        var photoSpec = SpecBodyOf(ts, "photo");
        var mediaSpec = SpecBodyOf(ts, "media");

        await Assert.That(photoSpec).Contains("base: this.media");
        await Assert.That(photoSpec).Contains("ignoredProperties: [\"name\"]");
        await Assert.That(photoSpec).DoesNotContain("json: \"Name\"");
        await Assert.That(mediaSpec).Contains("json: \"Name\"");
        await Assert.That(mediaSpec).DoesNotContain("ignoredProperties");
    }

    // The marker names the TS member of every overridden declaration, up
    // the whole linked chain.
    [Test]
    public async Task IgnoredOverrideOfALinkedChain_ListsEveryOverriddenTsName()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class Media
            {
                [Name("label")]
                public virtual string Name { get; init; } = "";
            }

            [Transpile]
            public class Picture : Media
            {
                public override string Name { get; init; } = "";
            }

            [Transpile]
            public sealed class Photo : Picture
            {
                [JsonIgnore]
                public override string Name { get; init; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert
            .That(SpecBodyOf(ts, "photo"))
            .Contains("ignoredProperties: [\"name\", \"label\"]");

        await Assert.That(SpecBodyOf(ts, "picture")).DoesNotContain("ignoredProperties");
    }

    // An unlinked spec already leaves the ignored base declaration out of
    // its inlined list, so it carries no marker.
    [Test]
    public async Task IgnoredOverrideOfInlinedBase_EmitsNoMarker()
    {
        var ts = TranspileContext(
            """
            public abstract class Media
            {
                public virtual string Name { get; init; } = "";
            }

            [Transpile]
            public sealed class Photo : Media
            {
                [JsonIgnore]
                public override string Name { get; init; } = "photo";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        var photoSpec = SpecBodyOf(ts, "photo");

        await Assert.That(photoSpec).Contains("type: Photo,");
        await Assert.That(photoSpec).DoesNotContain("json: \"Name\"");
        await Assert.That(ts).DoesNotContain("ignoredProperties");
    }

    // A constructor parameter bound to an ignored base property still gets a
    // factory argument; the payload carries no key for it, so it arrives as
    // undefined where System.Text.Json passes default(T) (a documented
    // divergence, DECIDED-20 a).
    [Test]
    public async Task IgnoredOverrideOfLinkedBase_FactoryStillReadsTheParameter()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class Media(string Name)
            {
                public virtual string Name { get; init; } = Name;
            }

            [Transpile]
            public sealed class Photo(string Name, string Path) : Media(Name)
            {
                [JsonIgnore]
                public override string Name { get; init; } = Name;
                public string Path { get; init; } = Path;
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        var photoSpec = SpecBodyOf(ts, "photo");

        await Assert.That(photoSpec).Contains("ignoredProperties: [\"name\"]");
        await Assert.That(photoSpec).Contains("new Photo(p.name as string, p.path as string)");
    }

    // The spec's `ts:` name and the factory read name the escaped property
    // the record declares (#253).
    [Test]
    public async Task ReservedWordProperty_UsesEscapedTsName()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Choice(int Default);

            [Transpile]
            [JsonSerializable(typeof(Choice))]
            """
        );

        await Assert.That(ts).Contains("ts: \"default_\"");
        await Assert.That(ts).Contains("json: \"Default\"");
        await Assert.That(ts).Contains("new Choice(p.default_ as number)");
    }

    [Test]
    public async Task ExplicitRootAlsoReachable_IsNotDuplicated()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public sealed record Root(Leaf Leaf);

            [Transpile]
            [JsonSerializable(typeof(Root))]
            [JsonSerializable(typeof(Leaf))]
            """
        );

        await Assert.That(CountGetters(ts, "leaf")).IsEqualTo(1);
        await Assert.That(ts.IndexOf("  get root(")).IsLessThan(ts.IndexOf("  get leaf("));
    }

    [Test]
    public async Task DictionaryKeyType_GetsItsOwnSpecGetter()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Keyed(int Id);

            [Transpile]
            public sealed record Root(Dictionary<Keyed, string> Names);

            [Transpile]
            [JsonSerializable(typeof(Root))]
            """
        );

        await Assert.That(CountGetters(ts, "keyed")).IsEqualTo(1);
        await Assert.That(ts).Contains("spec: () => this.keyed");
    }

    [Test]
    public async Task StaticProperty_IsNeitherSerializedNorWalked()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public sealed record Holder(int Z)
            {
                public static Leaf Shared { get; } = new("shared");
            }

            [Transpile]
            [JsonSerializable(typeof(Holder))]
            """
        );

        await Assert.That(CountGetters(ts, "holder")).IsEqualTo(1);
        await Assert.That(CountGetters(ts, "leaf")).IsEqualTo(0);
        await Assert.That(ts).DoesNotContain("\"shared\"");
        await Assert.That(ts).Contains("new Holder(p.z as number)");
    }

    // An interface needs polymorphic JSON and stays out of the closure.
    [Test]
    public async Task InterfacePropertyType_IsNotPulledIntoClosure()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public interface IShape { double Area { get; } }

            [Transpile]
            public sealed record Root(IShape Shape);

            [Transpile]
            [JsonSerializable(typeof(Root))]
            """
        );

        await Assert.That(CountGetters(ts, "root")).IsEqualTo(1);
        await Assert.That(CountGetters(ts, "iShape")).IsEqualTo(0);
    }

    // A closed generic is an object contract under its FR-031 name (FR-033).
    [Test]
    public async Task ClosedGenericPropertyType_IsPulledIntoClosure()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Box<T>(T Value);

            [Transpile]
            public sealed record Root(Box<int> Box);

            [Transpile]
            [JsonSerializable(typeof(Root))]
            """
        );

        await Assert.That(CountGetters(ts, "boxInt32")).IsEqualTo(1);
        await Assert.That(ts).Contains("spec: () => this.boxInt32");
    }

    // A base the spec does not link to (not opted in) has its properties
    // inlined; an overridden property is declared once, under its
    // most-derived declaration (FR-009).
    [Test]
    public async Task OverriddenPropertyOfInlinedBase_IsDeclaredOnce()
    {
        var ts = TranspileContext(
            """
            public abstract class Media
            {
                public virtual string Name { get; init; } = "";
                public int Size { get; init; }
            }

            [Transpile]
            public sealed class Photo : Media
            {
                public override string Name { get; init; } = "photo";
                public string Path { get; init; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert.That(ts).DoesNotContain("base: this.media");
        await Assert.That(CountOf(ts, "json: \"Name\"")).IsEqualTo(1);
        await Assert.That(CountOf(ts, "json: \"Size\"")).IsEqualTo(1);
        await Assert.That(CountOf(ts, "json: \"Path\"")).IsEqualTo(1);
    }

    [Test]
    public async Task HiddenPropertyOfInlinedBase_IsDeclaredOnce()
    {
        var ts = TranspileContext(
            """
            public abstract class Media
            {
                public string Name { get; init; } = "";
            }

            [Transpile]
            public sealed class Photo : Media
            {
                public new int Name { get; init; }
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert.That(CountOf(ts, "json: \"Name\"")).IsEqualTo(1);
        await Assert.That(ts).Contains("p.name as number");
    }

    // System.Text.Json writes an override renamed with [JsonPropertyName]
    // under both names (measured on .NET 10:
    // {"X":2,"n":"d","Name":"d","Base":1}).
    [Test]
    public async Task OverrideRenamedByJsonPropertyName_KeepsBothJsonNames()
    {
        var ts = TranspileContext(
            """
            public abstract class Media
            {
                public virtual string Name { get; init; } = "";
            }

            [Transpile]
            public sealed class Photo : Media
            {
                [JsonPropertyName("n")]
                public override string Name { get; init; } = "photo";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert.That(CountOf(ts, "json: \"Name\"")).IsEqualTo(1);
        await Assert.That(CountOf(ts, "json: \"n\"")).IsEqualTo(1);
    }

    // An ignored redeclaration hides the base declaration, as in
    // System.Text.Json, which writes neither.
    [Test]
    public async Task IgnoredOverrideOfInlinedBase_HidesTheBaseDeclaration()
    {
        var ts = TranspileContext(
            """
            public abstract class Media
            {
                public virtual string Name { get; init; } = "";
                public int Size { get; init; }
            }

            [Transpile]
            public sealed class Photo : Media
            {
                [JsonIgnore]
                public override string Name { get; init; } = "photo";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert.That(CountOf(ts, "json: \"Name\"")).IsEqualTo(0);
        await Assert.That(CountOf(ts, "json: \"Size\"")).IsEqualTo(1);
    }

    // Distinct properties that share a JSON name are not redeclarations, so
    // none of them is dropped (System.Text.Json rejects the type).
    [Test]
    public async Task DistinctPropertiesSharingAJsonName_AreBothKept()
    {
        var ts = TranspileContext(
            """
            public abstract class Media
            {
                [JsonPropertyName("x")]
                public int A { get; init; }
            }

            [Transpile]
            public sealed class Photo : Media
            {
                [JsonPropertyName("x")]
                public int B { get; init; }
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        await Assert.That(ts).Contains("ts: \"a\"");
        await Assert.That(ts).Contains("ts: \"b\"");
    }

    // With a linked base, each spec declares its own declaration once: the
    // base keeps its property for its own instances and the derived spec
    // carries the override.
    [Test]
    public async Task OverriddenPropertyOfLinkedBase_IsDeclaredOncePerSpec()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class Media
            {
                public virtual int Name { get; init; }
            }

            [Transpile]
            public sealed class Photo : Media
            {
                [JsonPropertyName("PhotoName")]
                public override int Name { get; init; }
                public string Path { get; init; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Photo))]
            """
        );

        var photoSpec = SpecBodyOf(ts, "photo");
        var mediaSpec = SpecBodyOf(ts, "media");

        await Assert.That(photoSpec).Contains("base: this.media");
        await Assert.That(CountOf(photoSpec, "json: \"PhotoName\"")).IsEqualTo(1);
        await Assert.That(photoSpec).DoesNotContain("json: \"Name\"");
        await Assert.That(CountOf(mediaSpec, "json: \"Name\"")).IsEqualTo(1);
        await Assert.That(mediaSpec).DoesNotContain("json: \"PhotoName\"");
    }

    [Test]
    public async Task NestedType_GetsIdentifierSafeGetterAndQualifiedTypeReference()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class Outer
            {
                [Transpile]
                public sealed record Inner(int V);

                [Transpile]
                public sealed record Other(int W);
            }

            [Transpile]
            public sealed record Timeline(Outer.Inner In, Outer.Other Ot);

            [Transpile]
            [JsonSerializable(typeof(Timeline))]
            [JsonSerializable(typeof(Outer.Inner))]
            """
        );

        // The explicit root and the refs agree on one plain-identifier getter
        // per type; the qualified name appears only in type positions.
        await Assert.That(CountGetters(ts, "inner")).IsEqualTo(1);
        await Assert.That(CountGetters(ts, "other")).IsEqualTo(1);
        await Assert.That(ts).Contains("spec: () => this.inner");
        await Assert.That(ts).Contains("spec: () => this.other");
        await Assert.That(ts).Contains("get inner(): TypeSpec<Outer.Inner>");
        await Assert.That(ts).Contains("type: Outer.Inner,");
        await Assert.That(ts).Contains("new Outer.Other(p.w as number)");
        await Assert.That(ts).DoesNotContain("get outer.");
        await Assert.That(ts).DoesNotContain("_outer.");
    }

    private static int CountOf(string text, string fragment) => text.Split(fragment).Length - 1;

    /// <summary>The <c>json:</c> names of a spec body, in order.</summary>
    private static List<string> JsonNamesOf(string specBody) =>
        Regex
            .Matches(specBody, "json: \"([^\"]*)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

    /// <summary>The getter of <paramref name="getterName"/> up to the next
    /// getter.</summary>
    private static string SpecBodyOf(string ts, string getterName)
    {
        var start = ts.IndexOf($"  get {getterName}(): ", StringComparison.Ordinal);
        var end = ts.IndexOf("\n  get ", start + 1, StringComparison.Ordinal);

        return end < 0 ? ts[start..] : ts[start..end];
    }

    private static int CountGetters(string ts, string getterName) =>
        ts.Split($"  get {getterName}(): ").Length - 1;
}
