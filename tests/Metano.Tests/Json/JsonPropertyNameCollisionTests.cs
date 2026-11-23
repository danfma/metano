using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// <c>MS0072</c> (#442): two properties of a contract that map to the same
/// JSON name. Every case below was measured on .NET 10 with a
/// source-generated context: System.Text.Json throws
/// <c>InvalidOperationException</c> "The JSON property name for 'Coll.id'
/// collides with another property." on the contract's first use.
/// </summary>
public class JsonPropertyNameCollisionTests
{
    [Test]
    public async Task PolicyFoldedNames_UnderWebDefaults_Collide()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public sealed class Coll { public string ID { get; set; } = ""; public string Id { get; set; } = ""; }
            """,
            "[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]",
            "Coll"
        );

        await Assert.That(errors).Count().IsEqualTo(1);

        await Assert
            .That(errors[0])
            .StartsWith("The JSON property name for 'Coll.id' collides with another property");
    }

    [Test]
    public async Task ExplicitName_CollidesWithAPolicyRenamedOne()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public sealed record Lab([property: JsonPropertyName("name")] string Label, string Name);
            """,
            "[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]",
            "Lab"
        );

        await Assert.That(errors).Count().IsEqualTo(1);

        await Assert.That(errors[0]).StartsWith("The JSON property name for 'Lab.name'");
    }

    [Test]
    public async Task InheritedProperty_CollidesWithADerivedOne()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public class BaseA { public int A { get; set; } }

            [Transpile]
            public sealed class Der : BaseA { public int a { get; set; } }
            """,
            "[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]",
            "Der"
        );

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SnakeCaseFoldedNames_Collide()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public sealed class Snake { public string FooBar { get; set; } = ""; public string Foo_Bar { get; set; } = ""; }
            """,
            "[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]",
            "Snake"
        );

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CaseOnlyDifference_CollidesWhenNamesAreReadIgnoringCase()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public sealed class Coll { public string ID { get; set; } = ""; public string Id { get; set; } = ""; }
            """,
            "[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]",
            "Coll"
        );

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CaseOnlyDifference_WithoutPolicy_DoesNotCollide()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public sealed class Coll { public string ID { get; set; } = ""; public string Id { get; set; } = ""; }
            """,
            "",
            "Coll"
        );

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Redeclaration_UnderTheSameName_DoesNotCollide()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public class Base2 { public virtual int X { get; set; } }

            [Transpile]
            public sealed class Der2 : Base2 { public override int X { get; set; } }
            """,
            "",
            "Der2"
        );

        await Assert.That(errors).IsEmpty();
    }

    private static List<string> CollisionsOf(string types, string options, string root)
    {
        var source = $$"""
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{types}}

            [Transpile]
            {{options}}
            [JsonSerializable(typeof({{root}}))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new JsonSerializerOptions()) { }
                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        return diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.JsonPropertyNameCollision)
            .Select(diagnostic => diagnostic.Message)
            .ToList();
    }
}
