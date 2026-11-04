using System.Text.RegularExpressions;

namespace Metano.Tests.Json;

/// <summary>
/// The context naming policy of <c>[JsonSourceGenerationOptions]</c> (spec 009
/// FR-048 item 1, FR-014, slice S2, #268): <c>JsonSerializerDefaults.Web</c>
/// sets camelCase, a named <c>PropertyNamingPolicy</c> overrides it,
/// <c>[JsonPropertyName]</c> wins over both, and the discriminator stays
/// verbatim.
///
/// Measured with a source-generated System.Text.Json context
/// (.NET SDK 10.0.401, runtime 10.0.12) over a class with one property per name:
/// <code>
/// Web (and (JsonSerializerDefaults)1, and General + CamelCase)
///   {"url","ioStream","htmlParser","id","x","alreadyCamel","value2","v2Value","item_Name",
///    "_Leading","abC_D","a1B","éCole","myURLValue","Explicit_Name"}
/// Web + PropertyNamingPolicy = SnakeCaseLower (same as SnakeCaseLower alone)
///   {"url","io_stream","html_parser","id","x","already_camel","value2","v2_value","item_name",
///    "_leading","abc_d","a1_b","é_cole","my_url_value","Explicit_Name"}
/// KebabCaseUpper
///   {"URL","IO-STREAM","HTML-PARSER","ID","X","ALREADY-CAMEL","VALUE2","V2-VALUE","ITEM_NAME",
///    "_LEADING","ABC_D","A1-B","É-COLE","MY-URL-VALUE","Explicit_Name"}
/// Web + PropertyNamingPolicy = Unspecified, General, no argument → names as declared
/// Web, [JsonDerivedType(typeof(Square), "square")] Shape
///   Square(2, "red") → {"$type":"square","sideLength":2,"fillColor":"red"}
/// Web, [JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")] Clip
///   ImageClip("a.png", 1.5) → {"Kind":"image","path":"a.png","seconds":1.5};
///   {"kind":"image",…} → NotSupportedException "The JSON payload for polymorphic interface or
///   abstract type 'Clip' must specify a type discriminator." (the tag is never renamed)
/// Web, Stats(3, 0.5) → {"count":3,"ratio":0.5}
/// Web, Names: "ID" and "Id" both camelCase to "id" → InvalidOperationException "The JSON
///   property name for 'Names.id' collides with another property."
/// </code>
/// The golden's generated TypeScript was bundled with <c>bun build</c> and
/// round-tripped under Node against the System.Text.Json output of the same
/// types: every write matched byte for byte and every STJ payload read back.
/// </summary>
public class JsonWebNamingTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new JsonSerializerOptions()) { }
            protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    private const string NamesRecord = """
        [Transpile]
        public sealed record Names(
            string URL,
            string IOStream,
            string HTMLParser,
            string ID,
            string X,
            string alreadyCamel,
            string Value2,
            string V2Value,
            string Item_Name,
            string _Leading,
            string ABC_D,
            string A1B,
            string ÉCole,
            string MyURLValue,
            [property: JsonPropertyName("Explicit_Name")] string Explicit
        );
        """;

    private const string GoldenDeclarations = """
        [Transpile]
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
        [JsonDerivedType(typeof(ImageClip), "image")]
        [JsonDerivedType(typeof(SilenceClip), "silence")]
        public abstract record Clip(double Seconds);

        [Transpile]
        public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

        [Transpile]
        public sealed record SilenceClip(double Seconds) : Clip(Seconds);

        [Transpile]
        [JsonDerivedType(typeof(Square), "square")]
        public abstract record Shape(string FillColor);

        [Transpile]
        public sealed record Square(int SideLength, string FillColor) : Shape(FillColor);

        [Transpile]
        public sealed record Stats(int Count, double Ratio, [property: JsonPropertyName("URL")] string Url);

        [Transpile]
        [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
        [JsonSerializable(typeof(Clip))]
        [JsonSerializable(typeof(Shape))]
        [JsonSerializable(typeof(Stats))]
        """;

    private static readonly string[] WebNames =
    [
        "url",
        "ioStream",
        "htmlParser",
        "id",
        "x",
        "alreadyCamel",
        "value2",
        "v2Value",
        "item_Name",
        "_Leading",
        "abC_D",
        "a1B",
        "éCole",
        "myURLValue",
        "Explicit_Name",
    ];

    private static readonly string[] DeclaredNames =
    [
        "URL",
        "IOStream",
        "HTMLParser",
        "ID",
        "X",
        "alreadyCamel",
        "Value2",
        "V2Value",
        "Item_Name",
        "_Leading",
        "ABC_D",
        "A1B",
        "ÉCole",
        "MyURLValue",
        "Explicit_Name",
    ];

    private static readonly string[] SnakeLowerNames =
    [
        "url",
        "io_stream",
        "html_parser",
        "id",
        "x",
        "already_camel",
        "value2",
        "v2_value",
        "item_name",
        "_leading",
        "abc_d",
        "a1_b",
        "é_cole",
        "my_url_value",
        "Explicit_Name",
    ];

    private static readonly string[] KebabUpperNames =
    [
        "URL",
        "IO-STREAM",
        "HTML-PARSER",
        "ID",
        "X",
        "ALREADY-CAMEL",
        "VALUE2",
        "V2-VALUE",
        "ITEM_NAME",
        "_LEADING",
        "ABC_D",
        "A1-B",
        "É-COLE",
        "MY-URL-VALUE",
        "Explicit_Name",
    ];

    [Test]
    public async Task WebContext_MatchesTheGolden()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).IsEqualTo(TranspileHelper.ReadExpected("json-web-context.ts"));
    }

    [Test]
    [Arguments("JsonSerializerDefaults.Web")]
    [Arguments("(JsonSerializerDefaults)1")]
    [Arguments(
        "JsonSerializerDefaults.General, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase"
    )]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase"
    )]
    public async Task CamelCaseContexts_NameLikeSystemTextJson(string options)
    {
        var ts = TranspileNames(options);

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(string.Join(',', WebNames));
    }

    [Test]
    [Arguments("")]
    [Arguments("JsonSerializerDefaults.General")]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified"
    )]
    public async Task ContextsWithoutAPolicy_KeepTheDeclaredNames(string options)
    {
        var ts = TranspileNames(options);

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(string.Join(',', DeclaredNames));
    }

    [Test]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower"
    )]
    [Arguments("PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower")]
    public async Task ExplicitSnakeCasePolicy_OverridesTheWebDefault(string options)
    {
        var ts = TranspileNames(options);

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(string.Join(',', SnakeLowerNames));
    }

    [Test]
    public async Task KebabCaseUpperPolicy_NamesLikeSystemTextJson()
    {
        var ts = TranspileNames("PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseUpper");

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(string.Join(',', KebabUpperNames));
    }

    // FR-048 item 1: [JsonPropertyName] wins over the Web default, and the TS
    // member keeps its own name.
    [Test]
    public async Task WebContext_KeepsAnExplicitJsonPropertyName()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(Regex.IsMatch(ts, "ts: \"url\",\\s+json: \"URL\",")).IsTrue();
        await Assert.That(Regex.IsMatch(ts, "ts: \"count\",\\s+json: \"count\",")).IsTrue();
    }

    // FR-014: the tag property is never renamed by the policy.
    [Test]
    public async Task WebContext_KeepsTheDiscriminatorVerbatim()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).Contains("""discriminator: "Kind",""");
        await Assert.That(ts).Contains("""discriminator: "$type",""");
    }

    private static string TranspileNames(string options) =>
        TranspileContext(
            $$"""
            {{NamesRecord}}

            [Transpile]
            [JsonSourceGenerationOptions({{options}})]
            [JsonSerializable(typeof(Names))]
            """
        );

    private static string JsonNamesOf(string ts) =>
        string.Join(
            ',',
            Regex.Matches(ts, "json: \"([^\"]+)\"").Select(match => match.Groups[1].Value)
        );

    private static string TranspileContext(string declarations)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}
            {{ContextBoilerplate}}
            """
        );

        if (diagnostics.Count > 0)
            throw new InvalidOperationException(
                "Unexpected diagnostics: "
                    + string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Format()))
            );

        return files[ContextPath];
    }
}
