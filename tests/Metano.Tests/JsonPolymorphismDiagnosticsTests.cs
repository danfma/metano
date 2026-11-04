using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>MS0032</c> (<c>InvalidJsonPolymorphism</c>) and <c>MS0033</c>
/// (<c>LossyJsonPolymorphism</c>) for class hierarchies (spec 009 FR-012,
/// FR-013, FR-015, FR-047; contracts/diagnostics.md), one test per rule.
/// System.Text.Json throws <c>InvalidOperationException</c> for every
/// <c>MS0032</c> rule it shares (research.md R1); <c>MS0033</c> cases behave
/// as in System.Text.Json and only warn.
/// </summary>
public class JsonPolymorphismDiagnosticsTests
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

    [Test]
    public async Task NotAssignableRegistration_IsReportedAndLeftOut()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            public sealed record Other(int X);

            [Transpile]
            [JsonDerivedType(typeof(ImageClip), "image")]
            [JsonDerivedType(typeof(Other), "x")]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'Other' registered by [JsonDerivedType] on 'Clip' is not assignable to 'Clip'."
        );

        await Assert.That(ts).Contains("spec: () => this.imageClip,");
        await Assert.That(ts).DoesNotContain("this.other");
    }

    [Test]
    public async Task InterfaceTypedRegistrationOnAClassBase_IsNotAssignable()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            public interface IRound { }

            [Transpile]
            [JsonDerivedType(typeof(IRound), "r")]
            public abstract record Clip(double Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'IRound' registered by [JsonDerivedType] on 'Clip' is not assignable to 'Clip'."
        );
    }

    [Test]
    public async Task UntranspiledRegistration_IsReported()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(Hidden), "h")]
            public abstract record Clip(double Seconds);

            public sealed record Hidden(double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'Hidden' registered by [JsonDerivedType] on 'Clip' is not transpiled, so it has no "
                + "TypeScript contract."
        );

        await Assert.That(ts).DoesNotContain("get hidden()");
    }

    [Test]
    public async Task AbstractRegistrationWithoutNearestAncestor_IsReported()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(AMid), "m")]
            public abstract record AB(int A);

            [Transpile]
            public abstract record AMid(int M, int A) : AB(A);

            [Transpile]
            [JsonSerializable(typeof(AB))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'AMid' is abstract; abstract derived types require UnknownDerivedTypeHandling = "
                + "FallBackToNearestAncestor on 'AB' (as in System.Text.Json)."
        );
    }

    [Test]
    public async Task DuplicateRegistration_KeepsTheFirst()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(ImageClip), "image")]
            [JsonDerivedType(typeof(ImageClip), "picture")]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await AssertSingleError(diagnostics, "'ImageClip' is registered twice on 'Clip'.");
        await Assert.That(ts).Contains("tag: \"image\",");
        await Assert.That(ts).DoesNotContain("picture");
    }

    [Test]
    public async Task DuplicateTag_KeepsTheFirst()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(ImageClip), "image")]
            [JsonDerivedType(typeof(SilenceClip), "image")]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

            [Transpile]
            public sealed record SilenceClip(double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "Type discriminator 'image' is registered twice on 'Clip'."
        );
        await Assert.That(ts).DoesNotContain("this.silenceClip");
    }

    [Test]
    public async Task IntAndStringTagsWithTheSameText_AreDistinct()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(IntA), 1)]
            [JsonDerivedType(typeof(IntS), "1")]
            public abstract record IntBase;

            [Transpile]
            public sealed record IntA(int X) : IntBase;

            [Transpile]
            public sealed record IntS(int Y) : IntBase;

            [Transpile]
            [JsonSerializable(typeof(IntBase))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("typeof(Box<int>)", "Box<int>")]
    [Arguments("typeof(Box<>)", "Box<>")]
    public async Task GenericRegistration_IsReported(string typeOf, string display)
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            $$"""
            [Transpile]
            [JsonDerivedType({{typeOf}}, "box")]
            public abstract record Clip(double Seconds);

            [Transpile]
            public sealed record Box<T>(T Item, double Seconds) : Clip(Seconds);

            [Transpile]
            [JsonSerializable(typeof(Clip))]
            """
        );

        await AssertSingleError(diagnostics, $"Generic derived type '{display}' is not supported.");
    }

    // FR-015: a registration is checked against every construction the
    // context reaches; GBase<int> is fine, GBase<string> is not.
    [Test]
    public async Task RegistrationNotAssignableToAReachedConstruction_IsReported()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(GDer), "g")]
            public abstract record GBase<T>(T Value);

            [Transpile]
            public sealed record GDer(int Value, int Extra) : GBase<int>(Value);

            [Transpile]
            [JsonSerializable(typeof(GBase<int>))]
            [JsonSerializable(typeof(GBase<string>))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'GDer' registered on 'GBase<T>' is not assignable to 'GBase<string>', which this "
                + "context reaches."
        );

        await Assert.That(ts).Contains("get gBaseString(): TypeSpec<GBase<string>>");
        await Assert.That(ts).Contains("spec: () => this.gDer,");
    }

    [Test]
    public async Task PropertyNamedLikeTheDiscriminator_LeavesNoDescriptor()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(KA), "a")]
            public abstract record KBase;

            [Transpile]
            public sealed record KA(string Kind) : KBase;

            [Transpile]
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
            [JsonSerializable(typeof(KBase))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'KA.Kind' serializes as 'kind', which conflicts with the discriminator of 'KBase'."
        );

        await Assert.That(ts).DoesNotContain("polymorphism");
    }

    [Test]
    public async Task PropertyDifferingFromTheDiscriminatorOnlyInCase_IsAllowed()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(KA), "a")]
            public abstract record KBase;

            [Transpile]
            public sealed record KA(string Kind) : KBase;

            [Transpile]
            [JsonSerializable(typeof(KBase))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(ts).Contains("polymorphism");
    }

    [Test]
    public async Task IntegerLikePropertyName_LeavesNoDescriptor()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(NumA), "a")]
            public abstract record NumBase(
                [property: JsonPropertyName("1")] int First
            );

            [Transpile]
            public sealed record NumA(int First) : NumBase(First);

            [Transpile]
            [JsonSerializable(typeof(NumBase))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "JSON property '1' of 'NumBase.First' is integer-like; JavaScript would order it "
                + "before the discriminator."
        );

        await Assert.That(ts).DoesNotContain("polymorphism");
    }

    [Test]
    public async Task UntaggedRegistration_Warns()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(NA))]
            public record NBase(int V);

            [Transpile]
            public sealed record NA(int A, int V) : NBase(V);

            [Transpile]
            [JsonSerializable(typeof(NBase))]
            """
        );

        await AssertSingle(
            diagnostics,
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.LossyJsonPolymorphism,
            "'NA' is registered on 'NBase' without a type discriminator; it serializes without a "
                + "tag and deserializes as 'NBase' (as in System.Text.Json)."
        );

        await Assert.That(ts).Contains("derived: [\n          {\n            spec: () => this.nA,");
    }

    [Test]
    public async Task PolymorphicRegistration_Warns()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(NMid), "mid")]
            public record NBase(int A);

            [Transpile]
            [JsonDerivedType(typeof(NLeaf), "leaf")]
            public record NMid(int B, int A) : NBase(A);

            [Transpile]
            public sealed record NLeaf(int C, int B, int A) : NMid(B, A);

            [Transpile]
            [JsonSerializable(typeof(NBase))]
            """
        );

        await AssertSingle(
            diagnostics,
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.LossyJsonPolymorphism,
            "'NMid' declares its own [JsonDerivedType]s; as in System.Text.Json, they apply only "
                + "through 'NMid' and are not re-dispatched through 'NBase'."
        );
    }

    [Test]
    public async Task SelfRegistration_IsValid()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(SBase), "b")]
            public record SBase(int V);

            [Transpile]
            [JsonSerializable(typeof(SBase))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(ts).Contains("spec: () => this.sBase,");
    }

    // FR-047: a registration whose type is not importable from the
    // referenced package is not transpiled in its declaring assembly.
    [Test]
    public async Task PackageRegistrationWithoutContract_IsReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            namespace Shared;

            [JsonDerivedType(typeof(Hidden), "h")]
            public abstract record Clip(double Seconds);

            [Ignore]
            public sealed record Hidden(double Seconds) : Clip(Seconds);
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

        await AssertSingleError(
            PolymorphismDiagnosticsOf(diagnostics),
            "'Hidden' registered by [JsonDerivedType] on 'Clip' is not transpiled, so it has no "
                + "TypeScript contract."
        );
    }

    private static Task AssertSingleError(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string message
    ) =>
        AssertSingle(
            diagnostics,
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.InvalidJsonPolymorphism,
            message
        );

    private static async Task AssertSingle(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        MetanoDiagnosticSeverity severity,
        string code,
        string message
    )
    {
        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Code).IsEqualTo(code);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(severity);
        await Assert.That(diagnostics[0].Message).IsEqualTo(message);
        await Assert.That(diagnostics[0].Location).IsNotNull();
    }

    private static (
        string Ts,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileWithPolymorphismDiagnostics(string declarations)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{ContextBoilerplate}}
            """
        );

        return (files[ContextPath], PolymorphismDiagnosticsOf(diagnostics));
    }

    private static List<MetanoDiagnostic> PolymorphismDiagnosticsOf(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(diagnostic =>
                diagnostic.Code
                    is DiagnosticCodes.InvalidJsonPolymorphism
                        or DiagnosticCodes.LossyJsonPolymorphism
            )
            .ToList();
}
