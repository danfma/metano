using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// Contracts declared in a referenced <c>[TranspileAssembly]</c> +
/// <c>[EmitPackage]</c> library and serialized through a context of the
/// consumer (spec 009 FR-010, FR-047): their transpilability is decided by
/// their declaring assembly, so library bases link through <c>base:</c>,
/// library types join the closure, and the context imports them from the
/// package and adds it to <c>package.json#dependencies</c>.
/// </summary>
public class JsonCrossPackageContextTests
{
    private const string ContextPath = "app/json-context.ts";

    private const string Library = """
        using System.Collections.Generic;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("shared", Version = "^2.0.0")]

        namespace Shared;

        public abstract record Clip(double Seconds);

        public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

        public sealed record Tag(string Name);

        public sealed record Page<T>(List<T> Items);
        """;

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task LibraryHierarchy_LinksTheLibraryBase()
    {
        var ts = TranspileContext("[JsonSerializable(typeof(ImageClip))]");

        await Assert.That(ts).Contains("get imageClip(): TypeSpec<ImageClip>");
        await Assert.That(ts).Contains("base: this.clip");
        await Assert.That(ts).Contains("get clip(): TypeSpec<Clip>");
        await Assert.That(ts).Contains("import { Clip, ImageClip } from \"shared/shared\";");
    }

    [Test]
    public async Task LibraryHierarchy_AddsThePackageDependency()
    {
        var dependencies = DependenciesOf("[JsonSerializable(typeof(ImageClip))]");

        await Assert.That(dependencies).ContainsKey("shared");
        await Assert.That(dependencies["shared"]).IsEqualTo("^2.0.0");
    }

    [Test]
    public async Task ConsumerTypeDerivingFromLibraryBase_LinksTheBaseAndReadsItsProperties()
    {
        var ts = TranspileContext(
            """
            [JsonSerializable(typeof(VideoClip))]
            """,
            "[Transpile] public sealed record VideoClip(string Url, double Seconds) : Clip(Seconds);"
        );

        await Assert.That(ts).Contains("get videoClip(): TypeSpec<VideoClip>");
        await Assert.That(ts).Contains("base: this.clip");
        await Assert.That(ts).Contains("get clip(): TypeSpec<Clip>");
        await Assert.That(ts).Contains("ts: \"seconds\"");
        await Assert.That(ts).Contains("new VideoClip(p.url as string, p.seconds as number)");
        await Assert.That(ts).Contains("import { Clip } from \"shared/shared\";");
    }

    [Test]
    public async Task ConsumerPropertyTypedWithLibraryRecord_PullsTheRecordIntoTheClosure()
    {
        var ts = TranspileContext(
            "[JsonSerializable(typeof(Holder))]",
            "[Transpile] public sealed record Holder(Tag Tag, List<Tag> Others);"
        );

        await Assert.That(ts).Contains("spec: () => this.tag");
        await Assert.That(ts).Contains("get tag(): TypeSpec<Tag>");
        await Assert.That(ts).Contains("import { Tag } from \"shared/shared\";");
    }

    [Test]
    public async Task LibraryElementOfValueRoot_IsDiscovered()
    {
        var ts = TranspileContext("[JsonSerializable(typeof(List<Tag>))]");

        await Assert.That(ts).Contains("get listTag(): ValueSpec<Tag[] | null>");
        await Assert.That(ts).Contains("get tag(): TypeSpec<Tag>");
        await Assert.That(ts).Contains("import { Tag } from \"shared/shared\";");
    }

    [Test]
    public async Task LibraryGenericClosedOverLibraryType_IsAdmitted()
    {
        var ts = TranspileContext("[JsonSerializable(typeof(Page<Tag>))]");

        await Assert.That(ts).Contains("get pageTag(): TypeSpec<Page<Tag>>");
        await Assert.That(ts).Contains("this.createSpec<Page<Tag>>(");
        await Assert.That(ts).Contains("spec: () => this.tag");
        await Assert.That(ts).Contains("import { Page, Tag } from \"shared/shared\";");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LibraryImage_LinksTheLibraryBase(bool referenceAssembly)
    {
        var files = TranspileHelper.TranspileWithLibraryImage(
            Library,
            Consumer("[JsonSerializable(typeof(ImageClip))]"),
            referenceAssembly
        );

        var ts = files[ContextPath];

        await Assert.That(ts).Contains("base: this.clip");
        await Assert.That(ts).Contains("get clip(): TypeSpec<Clip>");
        await Assert.That(ts).Contains("import { Clip, ImageClip } from \"shared/shared\";");
    }

    private static string Consumer(string roots, string declarations = "") =>
        $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Shared;

            namespace App;

            {{declarations}}

            [Transpile]
            {{roots}}
            {{ContextBoilerplate}}
            """;

    private static string TranspileContext(string roots, string declarations = "")
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            Consumer(roots, declarations)
        );

        var jsonDiagnostics = diagnostics
            .Where(diagnostic =>
                diagnostic.Code
                    is DiagnosticCodes.InvalidJsonSerializableRoot
                        or DiagnosticCodes.JsonTypeInfoNameCollision
            )
            .ToList();

        if (jsonDiagnostics.Count > 0)
            throw new InvalidOperationException(
                "Unexpected diagnostics: "
                    + string.Join("; ", jsonDiagnostics.Select(d => d.Format()))
            );

        return files[ContextPath];
    }

    /// <summary>
    /// The consumer's <c>package.json</c> dependencies when the context is
    /// its only type, so every library dependency comes from the context.
    /// </summary>
    private static IReadOnlyDictionary<string, string> DependenciesOf(string roots)
    {
        var library = TranspileHelper.CompileLibrary(Library);
        var consumer = TranspileHelper.CompileConsumer(Consumer(roots), library);
        var transformer = TranspileHelper.NewTransformer(consumer);

        transformer.TransformAll();

        return transformer.CrossPackageDependencies;
    }
}
