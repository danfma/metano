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
/// Web, (JsonSerializerDefaults)1 and General + CamelCase write the camelCase
///   column of MeasuredNames; Web + an explicit snake / kebab policy writes that
///   policy's column, the same as the policy alone (XMLHttpRequest, ΣIGMA and İd
///   from JsonNamingPolicy.*.ConvertName)
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
/// Since slice S2b the golden also carries the Web read options and the
/// <c>numberType</c> hints (<c>Reading</c>, <c>List&lt;uint&gt;</c>,
/// <c>short?</c>); their evidence is quoted in <see cref="JsonReadOptionsTests"/>.
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
            string XMLHttpRequest,
            string ΣIGMA,
            string İd,
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
        public enum Level { Low, High }

        [Transpile]
        public sealed record Reading(
            decimal Amount,
            long? Total,
            float Weight,
            IReadOnlyList<byte> Samples,
            Dictionary<ushort, sbyte> Offsets,
            Level Level,
            bool Valid
        );

        [Transpile]
        [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
        [JsonSerializable(typeof(Clip))]
        [JsonSerializable(typeof(Shape))]
        [JsonSerializable(typeof(Stats))]
        [JsonSerializable(typeof(Reading))]
        [JsonSerializable(typeof(List<uint>))]
        [JsonSerializable(typeof(short?))]
        """;

    private const int Declared = 0;

    private const int Camel = 1;

    private const int SnakeLower = 2;

    private const int SnakeUpper = 3;

    private const int KebabLower = 4;

    private const int KebabUpper = 5;

    // One row per Names property: the declared name, then the name .NET writes
    // under camelCase, snake_case lower / upper and kebab-case lower / upper.
    private static readonly string[][] MeasuredNames =
    [
        ["URL", "url", "url", "URL", "url", "URL"],
        ["IOStream", "ioStream", "io_stream", "IO_STREAM", "io-stream", "IO-STREAM"],
        ["HTMLParser", "htmlParser", "html_parser", "HTML_PARSER", "html-parser", "HTML-PARSER"],
        ["ID", "id", "id", "ID", "id", "ID"],
        ["X", "x", "x", "X", "x", "X"],
        [
            "alreadyCamel",
            "alreadyCamel",
            "already_camel",
            "ALREADY_CAMEL",
            "already-camel",
            "ALREADY-CAMEL",
        ],
        ["Value2", "value2", "value2", "VALUE2", "value2", "VALUE2"],
        ["V2Value", "v2Value", "v2_value", "V2_VALUE", "v2-value", "V2-VALUE"],
        ["Item_Name", "item_Name", "item_name", "ITEM_NAME", "item_name", "ITEM_NAME"],
        ["_Leading", "_Leading", "_leading", "_LEADING", "_leading", "_LEADING"],
        ["ABC_D", "abC_D", "abc_d", "ABC_D", "abc_d", "ABC_D"],
        ["A1B", "a1B", "a1_b", "A1_B", "a1-b", "A1-B"],
        ["ÉCole", "éCole", "é_cole", "É_COLE", "é-cole", "É-COLE"],
        [
            "MyURLValue",
            "myURLValue",
            "my_url_value",
            "MY_URL_VALUE",
            "my-url-value",
            "MY-URL-VALUE",
        ],
        [
            "XMLHttpRequest",
            "xmlHttpRequest",
            "xml_http_request",
            "XML_HTTP_REQUEST",
            "xml-http-request",
            "XML-HTTP-REQUEST",
        ],
        ["ΣIGMA", "σigma", "σigma", "ΣIGMA", "σigma", "ΣIGMA"],
        ["İd", "İd", "İd", "İD", "İd", "İD"],
        [
            "Explicit_Name",
            "Explicit_Name",
            "Explicit_Name",
            "Explicit_Name",
            "Explicit_Name",
            "Explicit_Name",
        ],
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

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(MeasuredColumn(Camel));
    }

    [Test]
    [Arguments("")]
    [Arguments("JsonSerializerDefaults.General")]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified"
    )]
    public async Task ContextsWithoutAnEffectivePolicy_KeepTheDeclaredNames(string options)
    {
        var ts = TranspileNames(options);

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(MeasuredColumn(Declared));
    }

    // Only System.Text.Json's attribute is read; a same-named one elsewhere is not.
    [Test]
    public async Task OptionsAttributeOfAnotherNamespace_IsIgnored()
    {
        var ts = TranspileContext(
            $$"""
            {{NamesRecord}}

            public static class Fake
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class JsonSourceGenerationOptionsAttribute(JsonSerializerDefaults defaults)
                    : Attribute
                {
                    public JsonSerializerDefaults Defaults { get; } = defaults;
                }
            }

            [Transpile]
            [Fake.JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
            [JsonSerializable(typeof(Names))]
            """
        );

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(MeasuredColumn(Declared));
    }

    // A same-named [JsonPropertyName] from another namespace leaves the policy in charge.
    [Test]
    public async Task PropertyNameAttributeOfAnotherNamespace_IsIgnored()
    {
        var ts = TranspileContext(
            """
            public static class Fake
            {
                [AttributeUsage(AttributeTargets.Property)]
                public sealed class JsonPropertyNameAttribute(string name) : Attribute
                {
                    public string Name { get; } = name;
                }
            }

            [Transpile]
            public sealed record Item([property: Fake.JsonPropertyName("Wire_Name")] string ItemName);

            [Transpile]
            [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
            [JsonSerializable(typeof(Item))]
            """
        );

        await Assert.That(JsonNamesOf(ts)).IsEqualTo("itemName");
    }

    [Test]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower",
        SnakeLower
    )]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseUpper",
        SnakeUpper
    )]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower",
        KebabLower
    )]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseUpper",
        KebabUpper
    )]
    [Arguments("PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower", SnakeLower)]
    [Arguments("PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseUpper", KebabUpper)]
    public async Task ExplicitPolicies_OverrideTheWebDefault(string options, int column)
    {
        var ts = TranspileNames(options);

        await Assert.That(JsonNamesOf(ts)).IsEqualTo(MeasuredColumn(column));
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

    private static string MeasuredColumn(int column) =>
        string.Join(',', MeasuredNames.Select(row => row[column]));

    private static string JsonNamesOf(string ts) =>
        string.Join(
            ',',
            Regex.Matches(ts, "json: \"([^\"]+)\"").Select(match => match.Groups[1].Value)
        );

    private static string TranspileContext(string declarations)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
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
