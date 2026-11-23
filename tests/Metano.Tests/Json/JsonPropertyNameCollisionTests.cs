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

    /// <summary>
    /// A <c>[JsonIgnore] new</c> member hides the base member of its CLR name
    /// in System.Text.Json, so another property may take that JSON name
    /// (measured on .NET 10: <c>{"Name":"o"}</c>).
    /// </summary>
    [Test]
    public async Task IgnoredNewHider_FreesTheBaseName()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public class GBase { public string Name { get; set; } = "b"; }

            [Transpile]
            public sealed class CaseQ : GBase
            {
                [JsonIgnore] public new string Name { get; set; } = "d";
                [JsonPropertyName("Name")] public string Other { get; set; } = "o";
            }
            """,
            "",
            "CaseQ"
        );

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task IgnoredNewHider_UnderCamelCase_FreesTheBaseName()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public class B2 { public string ID { get; set; } = ""; }

            [Transpile]
            public sealed class C2 : B2
            {
                [JsonIgnore] public new string ID { get; set; } = "";
                public string Id { get; set; } = "";
            }
            """,
            "[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]",
            "C2"
        );

        await Assert.That(errors).IsEmpty();
    }

    /// <summary>A <c>WhenWritingNull</c> hider is not an "always" ignore: the base member still collides.</summary>
    [Test]
    public async Task ConditionalIgnoreHider_StillCollides()
    {
        var errors = CollisionsOf(
            """
            [Transpile]
            public class B3 { public string? Name { get; set; } }

            [Transpile]
            public sealed class C3 : B3
            {
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public new string? Name { get; set; }
                [JsonPropertyName("Name")] public string Other { get; set; } = "";
            }
            """,
            "",
            "C3"
        );

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    /// <summary>
    /// The other hiding shapes, measured on .NET 10: a <c>new</c> without
    /// <c>[JsonIgnore]</c> and an override renamed with
    /// <c>[JsonPropertyName]</c> keep the base name (STJ throws); an ignored
    /// override frees it (<c>{"Name":"o"}</c>).
    /// </summary>
    [Test]
    [Arguments("public new string Name { get; set; } = \"d\";", 1)]
    [Arguments("[JsonPropertyName(\"n\")] public override string Name { get; set; } = \"d\";", 1)]
    [Arguments("[JsonIgnore] public override string Name { get; set; } = \"d\";", 0)]
    public async Task OtherHidingShapes_MatchSystemTextJson(string hider, int expected)
    {
        var errors = CollisionsOf(
            $$"""
            [Transpile]
            public class VBase { public virtual string Name { get; set; } = "b"; }

            [Transpile]
            public sealed class Der : VBase
            {
                {{hider}}
                [JsonPropertyName("Name")] public string Other { get; set; } = "o";
            }
            """,
            "",
            "Der"
        );

        await Assert.That(errors.Count).IsEqualTo(expected);
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
