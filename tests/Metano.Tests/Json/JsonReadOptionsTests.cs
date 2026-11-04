using System.Text.RegularExpressions;

namespace Metano.Tests.Json;

/// <summary>
/// The context read options of <c>[JsonSourceGenerationOptions]</c> (spec 009
/// FR-002, FR-048 items 2 and 4, slice S2b, #268): the <c>readOptions</c>
/// getter override and the <c>numberType</c> hints. The Web golden is
/// <c>json-web-context.ts</c> (<see cref="JsonWebNamingTests"/>).
///
/// Measured with source-generated System.Text.Json contexts (.NET SDK
/// 10.0.401, runtime 10.0.12), reading <c>Options</c> of each context:
/// <code>
/// General / no argument           → PropertyNameCaseInsensitive = false, NumberHandling = Strict,
///                                    AllowOutOfOrderMetadataProperties = false
/// Web                             → true, AllowReadingFromString, false
/// Web, PropertyNameCaseInsensitive = false → false, AllowReadingFromString, false
/// Web, NumberHandling = Strict    → true, Strict, false
/// General, PropertyNameCaseInsensitive = true, NumberHandling = AllowReadingFromString,
///   AllowOutOfOrderMetadataProperties = true → true, AllowReadingFromString, true
/// Web, AllowOutOfOrderMetadataProperties = true → true, AllowReadingFromString, true
/// </code>
/// Reads under those options: a Web <c>int</c> root, <c>List&lt;int&gt;</c>
/// element, <c>Dictionary&lt;string, int&gt;</c> value and record property read
/// <c>"5"</c>; <c>Web, NumberHandling = Strict</c> and <c>General</c> throw
/// "The JSON value could not be converted to System.Int32."; an enum property
/// given <c>"1"</c> and an int tag given <c>"1"</c> are rejected in every
/// context. <c>NumberHandling = AllowNamedFloatingPointLiterals</c> alone reads
/// <c>"NaN"</c> but not <c>"5"</c>, and <c>WriteAsString</c> writes <c>"5"</c>:
/// neither flag is mirrored (FR-046 reports them, slice S6b). Writes under Web
/// stay JSON numbers (<c>{"count":5,"ratio":2.5,"url":"u"}</c>).
///
/// The Web golden's generated TypeScript was bundled with <c>bun build</c> and
/// run under Node against the System.Text.Json output of the same types: every
/// write matched byte for byte, and every accepted and rejected payload of the
/// runtime tests (<c>read-options.test.ts</c>, <c>number-from-string.test.ts</c>)
/// behaved as in .NET.
/// </summary>
public class JsonReadOptionsTests
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

    private const string CaseInsensitive = "propertyNameCaseInsensitive: true";

    private const string NumbersFromStrings = "numberHandling: \"allowReadingFromString\"";

    private const string OutOfOrder = "allowOutOfOrderMetadataProperties: true";

    private const string Numbers = """
        [Transpile]
        public enum Level { Low, High }

        [Transpile]
        public sealed record Numbers(
            byte B,
            sbyte Sb,
            short S,
            ushort Us,
            int I,
            uint Ui,
            long L,
            ulong Ul,
            float F,
            double D,
            decimal M,
            string Text,
            bool Flag,
            Level Level,
            System.Guid Id,
            int? Maybe,
            List<long> Longs,
            Dictionary<int, double> ByKey
        );
        """;

    [Test]
    [Arguments("", "")]
    [Arguments("JsonSerializerDefaults.General", "")]
    [Arguments("JsonSerializerDefaults.Web", $"{CaseInsensitive}, {NumbersFromStrings}")]
    [Arguments("(JsonSerializerDefaults)1", $"{CaseInsensitive}, {NumbersFromStrings}")]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNameCaseInsensitive = false",
        NumbersFromStrings
    )]
    [Arguments(
        "JsonSerializerDefaults.Web, NumberHandling = JsonNumberHandling.Strict",
        CaseInsensitive
    )]
    [Arguments(
        "JsonSerializerDefaults.Web, PropertyNameCaseInsensitive = false, NumberHandling = JsonNumberHandling.Strict",
        ""
    )]
    [Arguments("PropertyNameCaseInsensitive = true", CaseInsensitive)]
    [Arguments("PropertyNameCaseInsensitive = false", "")]
    [Arguments("NumberHandling = JsonNumberHandling.AllowReadingFromString", NumbersFromStrings)]
    [Arguments(
        "NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString",
        NumbersFromStrings
    )]
    [Arguments("NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals", "")]
    [Arguments("NumberHandling = JsonNumberHandling.WriteAsString", "")]
    [Arguments("AllowOutOfOrderMetadataProperties = true", OutOfOrder)]
    [Arguments("AllowOutOfOrderMetadataProperties = false", "")]
    [Arguments(
        "JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true",
        $"{CaseInsensitive}, {NumbersFromStrings}, {OutOfOrder}"
    )]
    [Arguments(
        "PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString, AllowOutOfOrderMetadataProperties = true",
        $"{CaseInsensitive}, {NumbersFromStrings}, {OutOfOrder}"
    )]
    public async Task ContextOptions_EmitTheNonDefaultReadOptions(string options, string expected)
    {
        var ts = TranspileContext(options);

        await Assert.That(ReadOptionsOf(ts)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadOptionsOverride_IsAProtectedGetterTypedWithATypeOnlyImport()
    {
        var ts = TranspileContext("JsonSerializerDefaults.Web");

        await Assert.That(ts).Contains("protected override get readOptions(): JsonReadOptions {");

        await Assert
            .That(ts)
            .Contains(
                "import { SerializerContext, type JsonReadOptions, type TypeSpec } from \"metano-runtime/system/json\";"
            );
    }

    // FR-040: a context with default options is byte-identical to a context
    // without the attribute.
    [Test]
    [Arguments("JsonSerializerDefaults.General")]
    [Arguments("NumberHandling = JsonNumberHandling.Strict")]
    [Arguments("AllowOutOfOrderMetadataProperties = false")]
    public async Task DefaultOptions_LeaveTheContextUnchanged(string options)
    {
        var ts = TranspileContext(options);

        await Assert.That(ts).IsEqualTo(TranspileContext(""));
        await Assert.That(ts).DoesNotContain("readOptions");
        await Assert.That(ts).DoesNotContain("JsonReadOptions");
    }

    [Test]
    public async Task NumbersFromStrings_NameEveryNumericPrimitive()
    {
        var ts = TranspileContext("NumberHandling = JsonNumberHandling.AllowReadingFromString");

        string[] expected =
        [
            "b:System.Byte",
            "sb:System.SByte",
            "s:System.Int16",
            "us:System.UInt16",
            "i:System.Int32",
            "ui:System.UInt32",
            "l:System.Int64",
            "ul:System.UInt64",
            "f:System.Single",
            "d:System.Double",
            "maybe:System.Int32",
            "longs:System.Int64",
            "byKey:System.Int32",
            "byKey:System.Double",
        ];

        await Assert.That(NumberTypesOf(ts)).IsEquivalentTo(expected);
    }

    // The decimal descriptor carries no numberType: its converter reads the
    // context option and builds the value through its type.
    [Test]
    public async Task NumbersFromStrings_LeaveNonNumericDescriptorsBare()
    {
        var ts = TranspileContext("JsonSerializerDefaults.Web");

        await Assert.That(DescriptorOf(ts, "m")).IsEqualTo("{ kind: \"decimal\", type: Decimal }");
        await Assert.That(DescriptorOf(ts, "text")).IsEqualTo("{ kind: \"primitive\" }");
        await Assert.That(DescriptorOf(ts, "flag")).IsEqualTo("{ kind: \"primitive\" }");
        await Assert.That(DescriptorOf(ts, "level")).StartsWith("{ kind: \"numericEnum\"");
        await Assert.That(DescriptorOf(ts, "id")).StartsWith("{ kind: \"branded\"");
    }

    [Test]
    [Arguments("")]
    [Arguments("JsonSerializerDefaults.General")]
    [Arguments("JsonSerializerDefaults.Web, NumberHandling = JsonNumberHandling.Strict")]
    [Arguments("PropertyNameCaseInsensitive = true")]
    public async Task StrictNumberHandling_EmitsNoNumberType(string options)
    {
        var ts = TranspileContext(options);

        await Assert.That(ts).DoesNotContain("numberType");
    }

    [Test]
    public async Task NumberTypeHints_ReachValueRoots()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
            [JsonSerializable(typeof(int))]
            [JsonSerializable(typeof(List<double>))]
            [JsonSerializable(typeof(ulong?))]
            """,
            declarations: ""
        );

        await Assert
            .That(ts)
            .Contains("descriptor: { kind: \"primitive\", numberType: \"System.Int32\" },");
        await Assert
            .That(ts)
            .Contains("element: { kind: \"primitive\", numberType: \"System.Double\" },");
        await Assert
            .That(ts)
            .Contains("inner: { kind: \"primitive\", numberType: \"System.UInt64\" },");
    }

    // FR-002: the out-of-order option reaches a polymorphic context.
    [Test]
    public async Task OutOfOrderMetadata_IsReadForAPolymorphicContext()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonDerivedType(typeof(CDer), "d")]
            public record CBase(int V);

            [Transpile]
            public sealed record CDer(int V, int W) : CBase(V);

            [Transpile]
            [JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]
            [JsonSerializable(typeof(CBase))]
            """,
            declarations: ""
        );

        await Assert.That(ReadOptionsOf(ts)).IsEqualTo(OutOfOrder);
        await Assert.That(ts).Contains("discriminator: \"$type\",");
    }

    private static string TranspileContext(string options) =>
        TranspileContext(
            $$"""
            [Transpile]
            [JsonSourceGenerationOptions({{options}})]
            [JsonSerializable(typeof(Numbers))]
            """,
            Numbers
        );

    private static string TranspileContext(string contextAttributes, string declarations)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{contextAttributes}}
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

    /// <summary>The members of the <c>readOptions</c> literal, or empty without an override.</summary>
    private static string ReadOptionsOf(string ts)
    {
        var match = Regex.Match(
            ts,
            @"get readOptions\(\): JsonReadOptions \{\s*return \{(?<members>.*?)\};",
            RegexOptions.Singleline
        );

        return match.Success
            ? Regex.Replace(match.Groups["members"].Value.Trim(), @"\s+", " ")
            : "";
    }

    /// <summary><c>ts:numberType</c> for every numeric descriptor, in emission order.</summary>
    private static string[] NumberTypesOf(string ts) =>
        Regex
            .Matches(
                ts,
                """ts: "(?<ts>\w+)",\s+json: "\w+",\s+type: (?<type>\{[^;]*?\}),\s+(?:optional: true,\s+)?\}"""
            )
            .SelectMany(property =>
                Regex
                    .Matches(property.Groups["type"].Value, "numberType: \"(?<name>[\\w.]+)\"")
                    .Select(numberType =>
                        $"{property.Groups["ts"].Value}:{numberType.Groups["name"].Value}"
                    )
            )
            .ToArray();

    /// <summary>The single-line descriptor of property <paramref name="tsName"/>.</summary>
    private static string DescriptorOf(string ts, string tsName) =>
        Regex
            .Match(ts, $"ts: \"{tsName}\",\\s+json: \"\\w+\",\\s+type: (?<type>[^\\n]*?),?\\n")
            .Groups["type"]
            .Value;
}
