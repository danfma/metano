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
        await Assert
            .That(ts)
            .Contains(
                "throw new Error(\"Cannot deserialize abstract type 'Clip': polymorphic JSON deserialization is not supported yet.\");"
            );
    }

    [Test]
    public async Task DerivedType_FactoryPassesForwardedParamInConstructorOrder()
    {
        var ts = TranspileHelper.Transpile(ClipContextSource)["test-app/json-context.ts"];

        // `Seconds` is inherited but still a constructor slot, after `Path`.
        await Assert.That(ts).Contains("new ImageClip(p.path as string, p.seconds as number)");
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
            await Assert.That(ts.Split($"  get {getter}(): ").Length).IsEqualTo(2);
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

        await Assert.That(ts.Split("  get node(): ").Length).IsEqualTo(2);
        await Assert.That(ts.Split("  get edge(): ").Length).IsEqualTo(2);
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

        await Assert.That(ts.Split("  get leaf(): ").Length).IsEqualTo(2);
        await Assert.That(ts.IndexOf("  get root(")).IsLessThan(ts.IndexOf("  get leaf("));
    }
}
