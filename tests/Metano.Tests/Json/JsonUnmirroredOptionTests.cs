using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// <c>MS0037</c> (<c>UnmirroredJsonOption</c>, Warning) for every
/// <c>[JsonSourceGenerationOptions]</c> option and System.Text.Json attribute
/// the generated context does not mirror (spec 009 FR-046, DECIDED-25, slice
/// S6b, #268), and its absence for what it mirrors or what does not change
/// the JSON value.
///
/// The classification covers every writable property of
/// <c>JsonSourceGenerationOptionsAttribute</c> and every attribute of
/// <c>System.Text.Json.Serialization</c> on .NET 10 (SDK 10.0.401, read by
/// reflection): <c>AllowDuplicateProperties</c>,
/// <c>AllowOutOfOrderMetadataProperties</c>, <c>AllowTrailingCommas</c>,
/// <c>Converters</c>, <c>DefaultBufferSize</c>, <c>DefaultIgnoreCondition</c>,
/// <c>DictionaryKeyPolicy</c>, <c>GenerationMode</c>,
/// <c>IgnoreReadOnlyFields</c>, <c>IgnoreReadOnlyProperties</c>,
/// <c>IncludeFields</c>, <c>IndentCharacter</c>, <c>IndentSize</c>,
/// <c>MaxDepth</c>, <c>NewLine</c>, <c>NumberHandling</c>,
/// <c>PreferredObjectCreationHandling</c>, <c>PropertyNameCaseInsensitive</c>,
/// <c>PropertyNamingPolicy</c>, <c>ReadCommentHandling</c>,
/// <c>ReferenceHandler</c>, <c>RespectNullableAnnotations</c>,
/// <c>RespectRequiredConstructorParameters</c>, <c>UnknownTypeHandling</c>,
/// <c>UnmappedMemberHandling</c>, <c>UseStringEnumConverter</c>,
/// <c>WriteIndented</c>; and <c>[JsonConstructor]</c>, <c>[JsonConverter]</c>,
/// <c>[JsonDerivedType]</c>, <c>[JsonExtensionData]</c>, <c>[JsonIgnore]</c>,
/// <c>[JsonInclude]</c>, <c>[JsonNumberHandling]</c>,
/// <c>[JsonObjectCreationHandling]</c>, <c>[JsonPolymorphic]</c>,
/// <c>[JsonPropertyName]</c>, <c>[JsonPropertyOrder]</c>,
/// <c>[JsonRequired]</c>, <c>[JsonSerializable]</c>,
/// <c>[JsonSourceGenerationOptions]</c>, <c>[JsonStringEnumMemberName]</c>,
/// <c>[JsonUnmappedMemberHandling]</c>. Measured: a nullable
/// <c>[JsonRequired]</c> member missing from the payload throws
/// <c>JsonException</c> "JSON deserialization for type 'TestApp.Req1' was
/// missing required properties including: 'Note'."; <c>JsonIgnoreCondition</c>
/// has <c>Never</c>, <c>Always</c>, <c>WhenWritingDefault</c>,
/// <c>WhenWritingNull</c>, <c>WhenWriting</c> and <c>WhenReading</c>.
/// </summary>
public class JsonUnmirroredOptionTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    private const string Item = """
        [Transpile]
        public sealed record Item(string Name, int Count);
        """;

    [Test]
    [Arguments(
        "DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull",
        "DefaultIgnoreCondition"
    )]
    [Arguments("IgnoreReadOnlyProperties = true", "IgnoreReadOnlyProperties")]
    [Arguments("IgnoreReadOnlyFields = true", "IgnoreReadOnlyFields")]
    [Arguments("IncludeFields = true", "IncludeFields")]
    [Arguments("UseStringEnumConverter = true", "UseStringEnumConverter")]
    [Arguments("Converters = new[] { typeof(JsonStringEnumConverter) }", "Converters")]
    [Arguments("ReferenceHandler = JsonKnownReferenceHandler.Preserve", "ReferenceHandler")]
    [Arguments("DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase", "DictionaryKeyPolicy")]
    [Arguments("ReadCommentHandling = JsonCommentHandling.Skip", "ReadCommentHandling")]
    [Arguments("AllowTrailingCommas = true", "AllowTrailingCommas")]
    [Arguments("AllowDuplicateProperties = false", "AllowDuplicateProperties")]
    [Arguments(
        "UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow",
        "UnmappedMemberHandling"
    )]
    [Arguments("UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode", "UnknownTypeHandling")]
    [Arguments(
        "PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate",
        "PreferredObjectCreationHandling"
    )]
    [Arguments("RespectNullableAnnotations = true", "RespectNullableAnnotations")]
    [Arguments(
        "RespectRequiredConstructorParameters = true",
        "RespectRequiredConstructorParameters"
    )]
    [Arguments("MaxDepth = 8", "MaxDepth")]
    [Arguments("NumberHandling = JsonNumberHandling.WriteAsString", "NumberHandling.WriteAsString")]
    [Arguments(
        "NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals",
        "NumberHandling.AllowNamedFloatingPointLiterals"
    )]
    [Arguments(
        "NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString",
        "NumberHandling.WriteAsString"
    )]
    public async Task UnmirroredContextOption_IsReportedAtTheOption(string options, string setting)
    {
        var diagnostics = UnmirroredOf(ContextWith(options));

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(diagnostics[0].Message)
            .IsEqualTo(
                $"'{setting}' changes the JSON wire format, but the generated TypeScript "
                    + "context does not mirror it."
            );

        await Assert.That(SourceTextOf(diagnostics[0])).IsEqualTo(options);
    }

    [Test]
    public async Task EveryUnmirroredFlag_IsReportedOnce()
    {
        var diagnostics = UnmirroredOf(
            ContextWith(
                "NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowNamedFloatingPointLiterals"
            )
        );

        await Assert
            .That(string.Join(", ", diagnostics.Select(QuotedNameOf)))
            .IsEqualTo(
                "NumberHandling.WriteAsString, NumberHandling.AllowNamedFloatingPointLiterals"
            );
    }

    // Mirrored options (FR-048, FR-002), formatting and buffering options,
    // and every unmirrored option set to its System.Text.Json default.
    [Test]
    [Arguments("JsonSerializerDefaults.Web")]
    [Arguments("PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower")]
    [Arguments("PropertyNameCaseInsensitive = true")]
    [Arguments("NumberHandling = JsonNumberHandling.AllowReadingFromString")]
    [Arguments("NumberHandling = JsonNumberHandling.Strict")]
    [Arguments("AllowOutOfOrderMetadataProperties = true")]
    [Arguments("WriteIndented = true")]
    [Arguments("IndentCharacter = '\\t'")]
    [Arguments("IndentSize = 4")]
    [Arguments("NewLine = \"\\n\"")]
    [Arguments("DefaultBufferSize = 1024")]
    [Arguments("GenerationMode = JsonSourceGenerationMode.Metadata")]
    [Arguments("DefaultIgnoreCondition = JsonIgnoreCondition.Never")]
    [Arguments("IncludeFields = false")]
    [Arguments("AllowDuplicateProperties = true")]
    [Arguments("ReferenceHandler = JsonKnownReferenceHandler.Unspecified")]
    [Arguments("Converters = new System.Type[0]")]
    [Arguments("MaxDepth = 64")]
    [Arguments("MaxDepth = 0")]
    public async Task MirroredOrDefaultOption_IsNotReported(string options)
    {
        await Assert.That(UnmirroredOf(ContextWith(options))).IsEmpty();
    }

    [Test]
    public async Task MemberConverter_IsReportedAtTheAttribute()
    {
        await AssertSingleWarning(
            """
            public sealed class Upper : JsonConverter<string>
            {
                public override string Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options) => reader.GetString()!.ToUpperInvariant();
                public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.ToUpperInvariant());
            }

            [Transpile]
            public sealed class Doc
            {
                [JsonConverter(typeof(Upper))]
                public string Title { get; set; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Doc))]
            """,
            "'Doc.Title' has [JsonConverter]: the TypeScript context converts it as its type, "
                + "without the custom converter.",
            "JsonConverter(typeof(Upper))"
        );
    }

    [Test]
    public async Task TypeConverter_IsReportedOnAReachedType()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            [JsonConverter(typeof(JsonStringEnumConverter<Mood>))]
            public enum Mood { Calm, Loud }

            [Transpile]
            public sealed record Song(string Title, List<Mood> Moods);

            [Transpile]
            [JsonSerializable(typeof(Song))]
            """,
            "'Mood' has [JsonConverter]: the TypeScript context converts it with its built-in "
                + "descriptor, without the custom converter.",
            "JsonConverter(typeof(JsonStringEnumConverter<Mood>))"
        );
    }

    // A [Branded] type's descriptor writes the wrapped value, as the
    // converters such types carry do (DECIDED-25).
    [Test]
    public async Task ConverterOnABrandedType_IsNotReported()
    {
        var diagnostics = UnmirroredOf(
            """
            public sealed class TitleConverter : JsonConverter<Title>
            {
                public override Title Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options) => new(reader.GetString()!);
                public override void Write(System.Text.Json.Utf8JsonWriter writer, Title value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
            }

            [Branded]
            [JsonConverter(typeof(TitleConverter))]
            public readonly record struct Title(string Value);

            [Transpile]
            public sealed record Video(Title Title);

            [Transpile]
            [JsonSerializable(typeof(Video))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task MemberNumberHandling_IsReported()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            public sealed class Stats
            {
                [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
                public int Count { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Stats))]
            """,
            "'Stats.Count' has [JsonNumberHandling]: the TypeScript context applies the "
                + "context's number handling.",
            "JsonNumberHandling(JsonNumberHandling.WriteAsString)"
        );
    }

    [Test]
    public async Task TypeNumberHandling_IsReported()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public sealed record Stats(int Count);

            [Transpile]
            [JsonSerializable(typeof(Stats))]
            """,
            "'Stats' has [JsonNumberHandling]: the TypeScript context applies the context's "
                + "number handling to its members.",
            "JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)"
        );
    }

    [Test]
    public async Task DisallowedUnmappedMembers_AreReported()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
            public sealed record Strict(int A);

            [Transpile]
            [JsonSerializable(typeof(Strict))]
            """,
            "'Strict' has [JsonUnmappedMemberHandling(Disallow)]: the TypeScript context skips "
                + "unmapped JSON properties.",
            "JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)"
        );
    }

    [Test]
    public async Task PopulateOnAMember_IsReported()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            public sealed class Bag
            {
                [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
                public List<int> Items { get; set; } = [];
            }

            [Transpile]
            [JsonSerializable(typeof(Bag))]
            """,
            "'Bag.Items' has [JsonObjectCreationHandling(Populate)]: the TypeScript context "
                + "replaces the value on read.",
            "JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)"
        );
    }

    [Test]
    public async Task PopulateOnAType_IsReported()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
            public sealed class Bag
            {
                public List<int> Items { get; set; } = [];
            }

            [Transpile]
            [JsonSerializable(typeof(Bag))]
            """,
            "'Bag' has [JsonObjectCreationHandling(Populate)]: the TypeScript context replaces "
                + "the value on read.",
            "JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)"
        );
    }

    [Test]
    public async Task RequiredNullableMember_IsReported()
    {
        await AssertSingleWarning(
            """
            [Transpile]
            public sealed class Req
            {
                [JsonRequired]
                public string? Note { get; set; }

                [JsonRequired]
                public string Name { get; set; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(Req))]
            """,
            "'Req.Note' has [JsonRequired]: System.Text.Json rejects a payload without it, the "
                + "TypeScript context reads a missing nullable member as null.",
            "JsonRequired"
        );
    }

    [Test]
    public async Task JsonConstructorOnAnotherConstructor_IsReported()
    {
        var diagnostics = UnmirroredOf(
            """
            [Transpile]
            public sealed class Pair
            {
                public Pair(int a, int b) { A = a; B = b; }

                [JsonConstructor]
                public Pair(int a) : this(a, 0) { }

                public int A { get; }

                public int B { get; }
            }

            [Transpile]
            public sealed class Single
            {
                [JsonConstructor]
                public Single(int a) { A = a; }

                public int A { get; }
            }

            [Transpile]
            [JsonSerializable(typeof(Pair))]
            [JsonSerializable(typeof(Single))]
            """
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);

        await Assert
            .That(diagnostics[0].Message)
            .IsEqualTo(
                "'Pair' has [JsonConstructor] on a constructor the TypeScript context does not "
                    + "call: it constructs the type through its primary constructor."
            );
    }

    // Each member is reported once per context, however many contracts reach
    // it (a generic definition shares its constructions' members).
    [Test]
    public async Task SharedMember_IsReportedOnce()
    {
        var diagnostics = UnmirroredOf(
            """
            [Transpile]
            public class Box<T>
            {
                public T Value { get; set; } = default!;

                [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
                public int Size { get; set; }
            }

            [Transpile]
            public sealed class Shelf
            {
                public Box<string> Words { get; set; } = new();

                public Box<int> Numbers { get; set; } = new();
            }

            [Transpile]
            [JsonSerializable(typeof(Shelf))]
            """
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert
            .That(diagnostics[0].Message)
            .StartsWith("'Box<T>.Size' has [JsonNumberHandling]");
    }

    // A member System.Text.Json does not serialize carries no wire effect.
    [Test]
    public async Task AttributesOfAnIgnoredMember_AreNotReported()
    {
        var diagnostics = UnmirroredOf(
            """
            [Transpile]
            public sealed class Doc
            {
                [JsonIgnore]
                [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
                public int Hidden { get; set; }

                [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
                internal int Internal { get; set; }

                public int Shown { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Doc))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    // Mirrored attributes raise nothing.
    [Test]
    public async Task MirroredAttributes_AreNotReported()
    {
        var diagnostics = UnmirroredOf(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(Leaf), "leaf")]
            public abstract record Node([property: JsonPropertyName("n")] string Name);

            [Transpile]
            public sealed record Leaf(string Name) : Node(Name)
            {
                [JsonPropertyOrder(-1)]
                [JsonRequired]
                public int Weight { get; init; }

                [JsonInclude]
                internal int Rank { get; init; }

                [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
                public int Depth { get; init; }

                [JsonIgnore]
                public byte[] Cache { get; init; } = [];
            }

            [Transpile]
            [JsonSerializable(typeof(Node))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    // The warnings are reported once, by the emitting transform: the plans
    // computed for the runtime requirements and the constructed types stay
    // silent.
    [Test]
    public async Task ContextWarnings_AreReportedOnce()
    {
        var diagnostics = UnmirroredOf(
            ContextWith("IncludeFields = true, AllowTrailingCommas = true")
        );

        await Assert
            .That(string.Join(", ", diagnostics.Select(QuotedNameOf)))
            .IsEqualTo("IncludeFields, AllowTrailingCommas");
    }

    private static string ContextWith(string options) =>
        $$"""
            {{Item}}

            [Transpile]
            [JsonSourceGenerationOptions({{options}})]
            [JsonSerializable(typeof(Item))]
            """;

    private static async Task AssertSingleWarning(
        string declarations,
        string message,
        string attributeText
    )
    {
        var diagnostics = UnmirroredOf(declarations);

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(diagnostics[0].Message).IsEqualTo(message);
        await Assert.That(SourceTextOf(diagnostics[0])).IsEqualTo(attributeText);
    }

    /// <summary>The option or member a message names between its first quotes.</summary>
    private static string QuotedNameOf(MetanoDiagnostic diagnostic) =>
        diagnostic.Message.Split('\'')[1];

    private static string SourceTextOf(MetanoDiagnostic diagnostic)
    {
        var location = diagnostic.Location!;

        return location.SourceTree!.ToString()[location.SourceSpan.Start..location.SourceSpan.End];
    }

    /// <summary>
    /// The <c>MS0037</c> warnings of <paramref name="declarations"/>; any other
    /// diagnostic fails the test, and the context is still emitted.
    /// </summary>
    private static List<MetanoDiagnostic> UnmirroredOf(string declarations)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System;
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{ContextBoilerplate}}
            """
        );

        var unexpected = diagnostics
            .Where(diagnostic => diagnostic.Code != DiagnosticCodes.UnmirroredJsonOption)
            .ToList();

        if (unexpected.Count > 0 || !files.ContainsKey(ContextPath))
            throw new InvalidOperationException(
                "Unexpected diagnostics or no context: "
                    + string.Join("; ", unexpected.Select(diagnostic => diagnostic.Format()))
            );

        return diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption)
            .ToList();
    }
}
