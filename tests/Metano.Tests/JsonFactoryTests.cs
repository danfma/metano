using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>[JsonFactory]</c> on a branded type: the generated context's branded
/// descriptor calls the validating factory instead of <c>create</c>, and every
/// shape that cannot be called that way is <c>MS0034</c> (spec 009 User Story 3,
/// FR-020 to FR-026, #269).
/// </summary>
public class JsonFactoryTests
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

    private const string TitleWithFactory = """
        [Transpile, Branded]
        public readonly record struct Title(string Value)
        {
            [JsonFactory]
            public static Title From(string value)
            {
                if (value.Trim().Length == 0)
                {
                    throw new InvalidOperationException("blank");
                }

                return new Title(value.Trim());
            }
        }
        """;

    private const string GoldenDeclarations = """
        [Transpile, Branded]
        public readonly record struct Title(string Value)
        {
            [JsonFactory]
            public static Title From(string value)
            {
                if (value.Trim().Length == 0)
                {
                    throw new InvalidOperationException("blank");
                }

                return new Title(value.Trim());
            }
        }

        [Transpile, Branded]
        public readonly record struct Slug(string Value)
        {
            [JsonFactory, Name("parse")]
            private static Slug Normalize(string value) => new(value.ToLowerInvariant());
        }

        [Transpile, Branded]
        public readonly record struct Plain(string Value);

        [Transpile]
        public static class Episode
        {
            [Transpile, Branded]
            public readonly record struct Code(int Value)
            {
                [JsonFactory]
                public static Code From(int value) => new(value);
            }
        }

        [Transpile]
        public sealed record Video(
            Title Title,
            Slug Slug,
            Plain Note,
            Title? Subtitle,
            Title[] Aliases,
            Dictionary<Title, Episode.Code> Codes
        );

        [Transpile]
        [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        [JsonSerializable(typeof(Video))]
        [JsonSerializable(typeof(Title))]
        [JsonSerializable(typeof(List<Slug>))]
        [JsonSerializable(typeof(Episode.Code))]
        """;

    [Test]
    public async Task Factory_MatchesGolden()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).IsEqualTo(TranspileHelper.ReadExpected("json-context-factory.ts"));
    }

    [Test]
    public async Task PropertyDescriptor_CallsTheFactory()
    {
        var ts = TranspileContext(
            TitleWithFactory
                + """

                [Transpile]
                public sealed record Video(Title Title);

                [Transpile]
                [JsonSerializable(typeof(Video))]
                """
        );

        await Assert.That(ts).Contains("type: { kind: \"branded\", create: Title.from }");
        await Assert.That(ts).DoesNotContain("Title.create");
    }

    [Test]
    public async Task ValueRootDescriptor_CallsTheFactory()
    {
        var ts = TranspileContext(
            TitleWithFactory
                + """

                [Transpile]
                [JsonSerializable(typeof(Title))]
                """
        );

        await Assert.That(ts).Contains("get title(): ValueSpec<Title>");
        await Assert.That(ts).Contains("descriptor: { kind: \"branded\", create: Title.from }");
    }

    [Test]
    public async Task NameOverrideOnTheFactory_IsUsed()
    {
        var ts = TranspileContext(
            """
            [Transpile, Branded]
            public readonly record struct Title(string Value)
            {
                [JsonFactory, Name("parse")]
                public static Title From(string value) => new(value.Trim());
            }

            [Transpile]
            [JsonSerializable(typeof(Title))]
            """
        );

        await Assert.That(ts).Contains("create: Title.parse");
    }

    [Test]
    public async Task PrivateFactory_IsAccepted()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile, Branded]
            public readonly record struct Title(string Value)
            {
                [JsonFactory]
                private static Title Parse(string value) => new(value.Trim());
            }

            [Transpile]
            [JsonSerializable(typeof(Title))]
            """
        );

        await Assert.That(JsonFactoryDiagnostics(diagnostics)).IsEmpty();
        await Assert.That(files[ContextPath]).Contains("create: Title.parse");
    }

    [Test]
    public async Task NestedBrandedType_UsesTheQualifiedReference()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).Contains("create: Episode.Code.from");
    }

    [Test]
    public async Task NullableArrayAndMapKeyPositions_AllCallTheFactory()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).Contains("inner: { kind: \"branded\", create: Title.from }");
        await Assert.That(ts).Contains("element: { kind: \"branded\", create: Title.from }");
        await Assert.That(ts).Contains("key: { kind: \"branded\", create: Title.from }");
    }

    [Test]
    public async Task WithoutTheAttribute_CreateIsUnchanged()
    {
        var ts = TranspileContext(
            """
            [Transpile, Branded]
            public readonly record struct Plain(string Value);

            [Transpile]
            public sealed record Video(Plain Note, Guid Id);

            [Transpile]
            [JsonSerializable(typeof(Video))]
            [JsonSerializable(typeof(Plain))]
            """
        );

        await Assert.That(ts).Contains("create: Plain.create");
        await Assert.That(ts).Contains("create: UUID.create");
    }

    [Test]
    public async Task BrandedTypeFromAReferencedPackage_CallsTheFactoryAndIsImported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            namespace Shared;

            [Branded]
            public readonly record struct Title(string Value)
            {
                [JsonFactory]
                public static Title From(string value) => new(value.Trim());
            }
            """,
            $$"""
            using System.Text.Json.Serialization;
            using Shared;

            namespace App;

            [Transpile]
            public sealed record Video(Title Title);

            [Transpile]
            [JsonSerializable(typeof(Video))]
            {{ContextBoilerplate}}
            """
        );

        await Assert.That(JsonFactoryDiagnostics(diagnostics)).IsEmpty();
        await Assert
            .That(files[ContextPath.Replace("test-app", "app")])
            .Contains("create: Title.from");
    }

    [Test]
    public async Task InstanceMethod_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public Title From(string value) => new(value);
            """
        );

        await Assert.That(message).IsEqualTo("[JsonFactory] method 'Title.From' must be static.");
    }

    [Test]
    public async Task ConversionOperator_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static explicit operator Title(string value) => new(value);
            """
        );

        await Assert
            .That(message)
            .IsEqualTo(
                "[JsonFactory] cannot be applied to operator 'Title.op_Explicit'; use a named static method."
            );
    }

    [Test]
    public async Task WrongParameterCount_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static Title From(string value, int extra) => new(value);
            """
        );

        await Assert
            .That(message)
            .IsEqualTo("[JsonFactory] 'Title.From' must take exactly one 'string' parameter.");
    }

    [Test]
    public async Task WrongParameterType_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static Title From(int value) => new(value.ToString());
            """
        );

        await Assert
            .That(message)
            .IsEqualTo("[JsonFactory] 'Title.From' must take exactly one 'string' parameter.");
    }

    [Test]
    public async Task WrongReturnType_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static string From(string value) => value;
            """
        );

        await Assert.That(message).IsEqualTo("[JsonFactory] 'Title.From' must return 'Title'.");
    }

    [Test]
    public async Task OverloadedFactory_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static Title From(string value) => new(value);

            public static Title From(int value) => new(value.ToString());
            """
        );

        await Assert
            .That(message)
            .IsEqualTo(
                "[JsonFactory] 'Title.From' is overloaded; the TypeScript dispatcher could call a different overload."
            );
    }

    [Test]
    public async Task GenericFactory_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static Title From<T>(string value) => new(value);
            """
        );

        await Assert.That(message).IsEqualTo("[JsonFactory] 'Title.From' must not be generic.");
    }

    [Test]
    public async Task NotEmittedFactory_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            [Ignore]
            public static Title From(string value) => new(value);
            """
        );

        await Assert
            .That(message)
            .IsEqualTo("[JsonFactory] 'Title.From' is not emitted to TypeScript.");
    }

    [Test]
    public async Task FactoryNamedLikeASynthesizedMember_ReportsMs0034()
    {
        var message = await SingleMessage(
            """
            public static Title Create(string value) => new(value);
            """
        );

        await Assert
            .That(message)
            .IsEqualTo(
                "[JsonFactory] 'Title.Create' emits as 'create', which the branded companion already declares; rename it or add [Name]."
            );
    }

    [Test]
    public async Task TwoFactories_ReportsMs0034AtTheSecond()
    {
        var (_, diagnostics) = Transpile(
            """
            [Transpile, Branded]
            public readonly record struct Title(string Value)
            {
                [JsonFactory]
                public static Title From(string value) => new(value);

                [JsonFactory]
                public static Title Parse(string value) => new(value);
            }
            """
        );

        var errors = JsonFactoryDiagnostics(diagnostics);

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert
            .That(errors[0].Message)
            .IsEqualTo("'Title' declares more than one [JsonFactory] method.");
        await Assert.That(errors[0].Format()).Contains("(17,25)");
    }

    [Test]
    public async Task NonBrandedType_ReportsMs0034()
    {
        var (_, diagnostics) = Transpile(
            """
            [Transpile]
            public sealed record Foo(string Name)
            {
                [JsonFactory]
                public static Foo From(string value) => new(value);
            }
            """
        );

        var errors = JsonFactoryDiagnostics(diagnostics);

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert
            .That(errors[0].Message)
            .IsEqualTo("[JsonFactory] is only valid on a [Branded] type; 'Foo' is not branded.");
    }

    [Test]
    public async Task InvalidFactory_KeepsCreateAndPointsAtTheMethod()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile, Branded]
            public readonly record struct Title(string Value)
            {
                [JsonFactory]
                public Title From(string value) => new(value);
            }

            [Transpile]
            [JsonSerializable(typeof(Title))]
            """
        );

        var errors = JsonFactoryDiagnostics(diagnostics);

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(errors[0].Format()).Contains("(14,18)");
        await Assert.That(files[ContextPath]).Contains("create: Title.create");
    }

    private static async Task<string> SingleMessage(string members)
    {
        var (_, diagnostics) = Transpile(
            $$"""
            [Transpile, Branded]
            public readonly record struct Title(string Value)
            {
                [JsonFactory]
                {{members.ReplaceLineEndings("\n").Replace("\n", "\n    ")}}
            }
            """
        );

        var errors = JsonFactoryDiagnostics(diagnostics);

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(DiagnosticCodes.InvalidJsonFactory);

        return errors[0].Message;
    }

    private static List<MetanoDiagnostic> JsonFactoryDiagnostics(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(x => x.Code == DiagnosticCodes.InvalidJsonFactory).ToList();

    private static string WrapInNamespace(string declarations) =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{ContextBoilerplate}}
            """;

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string declarations) =>
        TranspileHelper.TranspileWithDiagnostics(WrapInNamespace(declarations));

    private static string TranspileContext(string declarations)
    {
        var (files, diagnostics) = Transpile(declarations);

        if (diagnostics.Any(x => x.Severity == MetanoDiagnosticSeverity.Error))
            throw new InvalidOperationException(
                "Unexpected diagnostics: " + string.Join("; ", diagnostics.Select(x => x.Format()))
            );

        return files[ContextPath];
    }
}
