using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Polymorphic class hierarchies in the generated <c>SerializerContext</c>
/// (spec 009 User Story 1, slice S6, #268): the <c>[JsonPolymorphic]</c> /
/// <c>[JsonDerivedType]</c> reader, the closure additions, the
/// <c>polymorphism</c> descriptor and closed generic bases.
///
/// The golden's wire behavior was checked against a source-generated
/// System.Text.Json context over the same C# types (.NET SDK 10.0.401):
/// <code>
/// Clip through clip           ImageClip("a.png", 1.5) → {"kind":"image","Path":"a.png","Seconds":1.5}
/// ImageClip through imageClip                         → {"Path":"a.png","Seconds":1.5}
/// Timeline("t", SilenceClip(2), [ImageClip("a", 1.5), SilenceClip(3)])
///   → {"Name":"t","Main":{"kind":"silence","Seconds":2},"Clips":[{"kind":"image","Path":"a","Seconds":1.5},{"kind":"silence","Seconds":3}]}
/// List&lt;Clip&gt;                  → [{"kind":"image","Path":"a","Seconds":1.5},{"kind":"silence","Seconds":2}]
/// IntBase                     IntA(5) → {"$type":1,"X":5}; IntS(2) → {"$type":"s","Y":2}
/// SBase (self-registered "b") SBase(1) → {"$type":"b","V":1}, reads back as SBase
/// GBase&lt;int&gt; ("g")          GDer(1, 2) → {"$type":"g","Extra":2,"Value":1}, reads back as GDer
/// AB (nearest ancestor)       AOther(3, 2, 1) → {"$type":"m","M":2,"A":1};
///                             reading {"$type":"m",…} → NotSupportedException "Deserialization of
///                             interface or abstract types is not supported. Type 'AMid'." (DECIDED-12)
/// IgnBase (ignore unknown)    {"$type":"zz","V":1} → IgnBase
/// </code>
/// The generated TypeScript of this golden was compiled with
/// <c>tsgo --strict</c> and round-tripped every row above under Node.
/// </summary>
public class JsonPolymorphicContextTests
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

    private const string GoldenDeclarations = """
        [Transpile]
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(ImageClip), "image")]
        [JsonDerivedType(typeof(SilenceClip), "silence")]
        public abstract record Clip(double Seconds);

        [Transpile]
        public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

        [Transpile]
        public sealed record SilenceClip(double Seconds) : Clip(Seconds);

        [Transpile]
        public sealed record Timeline(string Name, Clip Main, IReadOnlyList<Clip> Clips);

        [Transpile]
        [JsonDerivedType(typeof(IntA), 1)]
        [JsonDerivedType(typeof(IntS), "s")]
        public abstract record IntBase;

        [Transpile]
        public sealed record IntA(int X) : IntBase;

        [Transpile]
        public sealed record IntS(int Y) : IntBase;

        [Transpile]
        [JsonDerivedType(typeof(SBase), "b")]
        [JsonDerivedType(typeof(SDer), "d")]
        public record SBase(int V);

        [Transpile]
        public sealed record SDer(int V, int W) : SBase(V);

        [Transpile]
        [JsonDerivedType(typeof(GDer), "g")]
        public abstract record GBase<T>(T Value);

        [Transpile]
        public sealed record GDer(int Value, int Extra) : GBase<int>(Value);

        [Transpile]
        [JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
        [JsonDerivedType(typeof(AMid), "m")]
        [JsonDerivedType(typeof(ALeaf), "l")]
        public abstract record AB(int A);

        [Transpile]
        public abstract record AMid(int M, int A) : AB(A);

        [Transpile]
        public sealed record ALeaf(int L, int M, int A) : AMid(M, A);

        [Transpile]
        [JsonPolymorphic(
            IgnoreUnrecognizedTypeDiscriminators = true,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType
        )]
        [JsonDerivedType(typeof(IgnA), "a")]
        public record IgnBase(int V);

        [Transpile]
        public sealed record IgnA(int V) : IgnBase(V);

        [Transpile]
        [JsonSerializable(typeof(Timeline))]
        [JsonSerializable(typeof(List<Clip>))]
        [JsonSerializable(typeof(IntBase))]
        [JsonSerializable(typeof(SBase))]
        [JsonSerializable(typeof(GBase<int>))]
        [JsonSerializable(typeof(AB))]
        [JsonSerializable(typeof(IgnBase))]
        """;

    [Test]
    public async Task PolymorphicHierarchies_MatchTheGolden()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert
            .That(ts)
            .IsEqualTo(TranspileHelper.ReadExpected("json-polymorphic-context.ts"));
    }

    // FR-005: dispatch applies through every descriptor that reaches the base,
    // so each collection root links to the polymorphic spec, not to a variant.
    [Test]
    public async Task CollectionRoots_LinkToThePolymorphicBase()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonDerivedType(typeof(ImageClip), "image")]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(List<Clip>))]
            [JsonSerializable(typeof(Clip[]))]
            [JsonSerializable(typeof(Dictionary<string, Clip>))]
            """
        );

        await Assert.That(ts).Contains("get listClip(): ValueSpec<Clip[] | null>");
        await Assert.That(ts).Contains("get clipArray(): ValueSpec<Clip[] | null>");

        await Assert
            .That(ts)
            .Contains("get dictionaryStringClip(): ValueSpec<Map<string, Clip> | null>");

        await Assert.That(ts.Split("spec: () => this.clip,").Length - 1).IsEqualTo(3);
        await Assert.That(ts).Contains("spec: () => this.imageClip,");
    }

    [Test]
    public async Task DerivedTypes_BringTheirPropertyTypesIntoTheClosure()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Asset(string Url);

            [Transpile]
            [JsonDerivedType(typeof(ImageClip), "image")]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(Asset Image, double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await Assert.That(ts).Contains("get imageClip(): TypeSpec<ImageClip>");
        await Assert.That(ts).Contains("get asset(): TypeSpec<Asset>");
        await Assert.That(ts).Contains("spec: () => this.asset");
    }

    [Test]
    public async Task NestedVariants_UseTheirQualifiedTypeAndOwnGetter()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonDerivedType(typeof(Shape.Circle), "circle")]
            public abstract record Shape
            {
                [Transpile]
                public sealed record Circle(double Radius) : Shape;
            }

            [Transpile]
            [JsonSerializable(typeof(Shape))]
            """
        );

        await Assert.That(ts).Contains("tag: \"circle\",\n            spec: () => this.circle,");
        await Assert.That(ts).Contains("get circle(): TypeSpec<Shape.Circle>");
        await Assert.That(ts).Contains("type: Shape.Circle,");
    }

    // DECIDED-22 j: a base reached only through a `base` link is never
    // dispatched through, so System.Text.Json never builds its polymorphism.
    [Test]
    public async Task BaseReachedOnlyThroughABaseLink_IsNotPolymorphic()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonDerivedType(typeof(GDer), "g")]
            public abstract record GBase<T>(T Value);

            [Transpile]
            public sealed record GDer(int Value, int Extra) : GBase<int>(Value);

            [Transpile]
            [JsonSerializable(typeof(GDer))]
            """
        );

        await Assert.That(ts).Contains("base: this.gBaseInt32,");
        await Assert.That(ts).Contains("get gBaseInt32(): TypeSpec<GBase<number>>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");
    }

    // FR-015: the constructed base is reached through a property after its
    // derived type's `base` link walked it, so it is expanded then.
    [Test]
    public async Task ClosedGenericBase_ReachedThroughAPropertyLater_IsPolymorphic()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonDerivedType(typeof(GDer), "g")]
            public abstract record GBase<T>(T Value);

            [Transpile]
            public sealed record GDer(int Value, int Extra) : GBase<int>(Value);

            [Transpile]
            public sealed record Holder(GBase<int> Item);

            [Transpile]
            [JsonSerializable(typeof(GDer))]
            [JsonSerializable(typeof(Holder))]
            """
        );

        await Assert.That(ts).Contains("get gBaseInt32(): TypeSpec<GBase<number>>");
        await Assert.That(ts).Contains("tag: \"g\",\n            spec: () => this.gDer,");
    }

    [Test]
    public async Task AssemblyWideTranspile_DiscoversUnlistedDerivedTypes()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]

            namespace TestApp;

            [JsonDerivedType(typeof(ImageClip), "image")]
            public abstract record Clip(double Seconds);

            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [JsonSerializable(typeof(Clip))]
            {{ContextBoilerplate}}
            """
        );

        await Assert.That(diagnostics.Where(IsPolymorphismDiagnostic)).IsEmpty();
        await Assert.That(files[ContextPath]).Contains("spec: () => this.imageClip");
        await Assert.That(files[ContextPath]).Contains("base: this.clip,");
    }

    // FR-047: the base and its variants live in a referenced Metano package;
    // the consumer's context emits their getters and imports them.
    [Test]
    public async Task PackageHierarchy_IsPolymorphicInTheConsumerContext()
    {
        var files = TranspileHelper.TranspileWithLibrary(
            """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            namespace Shared;

            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(ImageClip), "image")]
            public abstract record Clip(double Seconds);

            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);
            """,
            $$"""
            using System.Text.Json.Serialization;
            using Shared;

            namespace App;

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            {{ContextBoilerplate}}
            """
        );

        var ts = files["app/json-context.ts"];

        await Assert.That(ts).Contains("discriminator: \"kind\",");
        await Assert.That(ts).Contains("spec: () => this.imageClip,");
        await Assert.That(ts).Contains("import { Clip, ImageClip } from \"shared/shared\";");
    }

    // A derived type that registers its own variants keeps its own
    // descriptor, which applies only through its own spec (FR-006, FR-013).
    [Test]
    public async Task NestedPolymorphicBase_KeepsItsOwnDescriptor()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
            [JsonDerivedType(typeof(NMid), "mid")]
            public record NBase(int A);

            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "u")]
            [JsonDerivedType(typeof(NLeaf), "leaf")]
            public record NMid(int B, int A) : NBase(A);

            [Transpile]
            public sealed record NLeaf(int C, int B, int A) : NMid(B, A);

            [Transpile]
            [JsonSerializable(typeof(NBase))]
            """
        );

        var ts = files[ContextPath];

        await Assert.That(ts).Contains("discriminator: \"t\",");
        await Assert.That(ts).Contains("discriminator: \"u\",");
        await Assert.That(ts).Contains("get nLeaf(): TypeSpec<NLeaf>");

        await Assert
            .That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.LossyJsonPolymorphism);
    }

    // Interface bases and [PlainObject] variants are slice S6c (FR-018,
    // FR-019): until then their contexts are unchanged, with an MS0033 warning.
    [Test]
    public async Task PlainObjectHierarchy_IsLeftForStructuralDispatch()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(Point), "point")]
            public abstract record Shape;

            [Transpile, PlainObject]
            public sealed record Point(double X) : Shape;

            [Transpile]
            [JsonSerializable(typeof(Shape))]
            """
        );

        var ts = files[ContextPath];
        var warnings = diagnostics.Where(IsPolymorphismDiagnostic).ToList();

        await Assert.That(ts).Contains("get shape(): TypeSpec<Shape>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");
        await Assert.That(ts).DoesNotContain("get point()");
        await Assert.That(warnings).Count().IsEqualTo(1);
        await Assert.That(warnings[0].Code).IsEqualTo(DiagnosticCodes.LossyJsonPolymorphism);

        await Assert
            .That(warnings[0].Message)
            .IsEqualTo(
                "Polymorphism for interface and [PlainObject] types is not supported yet; "
                    + "'Shape' is emitted without a discriminator."
            );
    }

    [Test]
    public async Task NonPolymorphicAbstractType_KeepsItsFactoryMessage()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public abstract record Clip(double Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await Assert.That(ts).Contains("get clip(): TypeSpec<Clip>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");

        await Assert
            .That(ts)
            .Contains(
                "Cannot deserialize abstract type 'Clip': polymorphic JSON deserialization is not supported yet."
            );
    }

    private static string WrapInNamespace(string declarations) =>
        $$"""
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
        var jsonDiagnostics = diagnostics.Where(IsPolymorphismDiagnostic).ToList();

        if (jsonDiagnostics.Count > 0)
        {
            var formatted = string.Join("; ", jsonDiagnostics.Select(d => d.Format()));

            throw new InvalidOperationException("Unexpected diagnostics: " + formatted);
        }

        return files[ContextPath];
    }

    private static bool IsPolymorphismDiagnostic(MetanoDiagnostic diagnostic) =>
        diagnostic.Code
            is DiagnosticCodes.InvalidJsonPolymorphism
                or DiagnosticCodes.LossyJsonPolymorphism
                or DiagnosticCodes.InvalidJsonSerializableRoot
                or DiagnosticCodes.JsonTypeInfoNameCollision;
}
