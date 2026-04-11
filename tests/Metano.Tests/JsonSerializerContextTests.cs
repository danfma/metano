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

        await Assert.That(result).ContainsKey("json-context.ts");
        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

        await Assert.That(ts).Contains("ts: \"name\"");
        await Assert.That(ts).DoesNotContain("internalToken");
        await Assert.That(ts).DoesNotContain("InternalToken");
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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

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

        var ts = result["json-context.ts"];

        // Guid should be classified as a branded descriptor pointing at UUID.create
        await Assert.That(ts).Contains("kind: \"branded\"");
        await Assert.That(ts).Contains("create: UUID.create");
        // And UUID should be imported from metano-runtime
        await Assert.That(ts).Contains("import { UUID } from \"metano-runtime\"");
    }
}
