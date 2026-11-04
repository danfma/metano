using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>MS0032</c> (<c>InvalidJsonPolymorphism</c>) and <c>MS0033</c>
/// (<c>LossyJsonPolymorphism</c>) for class, interface and
/// <c>[PlainObject]</c> hierarchies (spec 009 FR-012, FR-013, FR-015,
/// FR-018, FR-019, FR-047; contracts/diagnostics.md), one test per rule.
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
        await Assert.That(ts).DoesNotContain("spec: () => this.other,");
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

        await Assert.That(ts).Contains("polymorphism: {");
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
        await Assert.That(ts).DoesNotContain("tag: \"picture\"");
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

        await Assert.That(ts).Contains("spec: () => this.imageClip,");
        await Assert.That(ts).DoesNotContain("spec: () => this.silenceClip,");
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

        await Assert.That(ts).Contains("get kBase(): TypeSpec<KBase>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");
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
        await Assert.That(ts).Contains("polymorphism: {");
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

        await Assert.That(ts).Contains("get numBase(): TypeSpec<NumBase>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");
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

    [Test]
    public async Task UntaggedRegistrationOnAnAbstractBase_WarnsThatItCannotBeRead()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(UAD))]
            public abstract record UAB(int A);

            [Transpile]
            public sealed record UAD(int A) : UAB(A);

            [Transpile]
            [JsonSerializable(typeof(UAB))]
            """
        );

        await AssertSingle(
            diagnostics,
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.LossyJsonPolymorphism,
            "'UAD' is registered on 'UAB' without a type discriminator; it serializes without a "
                + "tag and cannot be deserialized through the abstract 'UAB' (as in "
                + "System.Text.Json)."
        );
    }

    // System.Text.Json writes {"kind":"x","V":1} for the untagged KUD: an
    // untagged contract is never written with a tag, so it cannot conflict.
    [Test]
    public async Task PropertyNamedLikeTheDiscriminatorOnAnUntaggedRegistration_IsAllowed()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(KUD))]
            [JsonDerivedType(typeof(KUE), "e")]
            public record KUB(int V);

            [Transpile]
            public record KUD(int V, [property: JsonPropertyName("kind")] string Kind2) : KUB(V);

            [Transpile]
            public record KUE(int V) : KUB(V);

            [Transpile]
            [JsonSerializable(typeof(KUB))]
            """
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .DoesNotContain(DiagnosticCodes.InvalidJsonPolymorphism);
        await Assert.That(ts).Contains("polymorphism: {");
    }

    // A base property inherited by a tagged registration conflicts, as
    // System.Text.Json reports it on the registration (KBBD).
    [Test]
    public async Task InheritedPropertyNamedLikeTheDiscriminator_IsReported()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(KBBD), "d")]
            public record KBB([property: JsonPropertyName("kind")] string K);

            [Transpile]
            public record KBBD(string K) : KBB(K);

            [Transpile]
            [JsonSerializable(typeof(KBB))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'KBB.K' serializes as 'kind', which conflicts with the discriminator of 'KBB'."
        );
    }

    [Test]
    public async Task HierarchyWithoutTags_HasNoDiscriminatorConflicts()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(DUD))]
            public record DU([property: JsonPropertyName("1")] int X, [property: JsonPropertyName("$type")] int Y);

            [Transpile]
            public record DUD(int X, int Y) : DU(X, Y);

            [Transpile]
            [JsonSerializable(typeof(DU))]
            """
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .DoesNotContain(DiagnosticCodes.InvalidJsonPolymorphism);
        await Assert.That(ts).Contains("polymorphism: {");
    }

    // One conflict shared by GBase<int> and GBase<string> is reported once.
    [Test]
    public async Task ConflictSharedByTwoConstructions_IsReportedOnce()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(GInt), "i")]
            [JsonDerivedType(typeof(GStr), "s")]
            public abstract record GBase<T>([property: JsonPropertyName("kind")] T Value);

            [Transpile]
            public sealed record GInt(int Value) : GBase<int>(Value);

            [Transpile]
            public sealed record GStr(string Value) : GBase<string>(Value);

            [Transpile]
            [JsonSerializable(typeof(GBase<int>))]
            [JsonSerializable(typeof(GBase<string>))]
            """
        );

        var conflicts = diagnostics.Where(d => d.Message.Contains("conflicts with")).ToList();

        await Assert.That(conflicts).Count().IsEqualTo(1);

        await Assert
            .That(conflicts[0].Message)
            .IsEqualTo(
                "'GBase<T>.Value' serializes as 'kind', which conflicts with the discriminator of "
                    + "'GBase<T>'."
            );
    }

    // System.Text.Json: "Specified type 'SealedB' does not support polymorphism.
    // Polymorphic types cannot be structs, sealed types, generic types or
    // System.Object." (a struct cannot carry the attribute: CS0592).
    [Test]
    public async Task SealedBase_IsReported()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(SealedB), "s")]
            public sealed record SealedB(int V);

            [Transpile]
            [JsonSerializable(typeof(SealedB))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'SealedB' does not support polymorphism: polymorphic types cannot be sealed types "
                + "(as in System.Text.Json)."
        );

        await Assert.That(ts).Contains("get sealedB(): TypeSpec<SealedB>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");
    }

    // System.Text.Json: "Polymorphic configuration for type 'PolyOnly' should
    // specify at least one derived type."
    [Test]
    public async Task PolymorphicAttributeWithoutRegistrations_IsReported()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonPolymorphic]
            public record PolyOnly(int V);

            [Transpile]
            [JsonSerializable(typeof(PolyOnly))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "Polymorphic configuration for type 'PolyOnly' should specify at least one derived "
                + "type (as in System.Text.Json)."
        );
    }

    // System.Text.Json: "The metadata property names '$id', '$ref', and
    // '$values' are reserved and cannot be used as custom type discriminator
    // property names."
    [Test]
    [Arguments("$id")]
    [Arguments("$ref")]
    [Arguments("$values")]
    public async Task ReservedDiscriminator_IsReported(string discriminator)
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            $$"""
            [Transpile]
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "{{discriminator}}")]
            [JsonDerivedType(typeof(IdDiscD), "d")]
            public abstract record IdDisc;

            [Transpile]
            public sealed record IdDiscD(int V) : IdDisc;

            [Transpile]
            [JsonSerializable(typeof(IdDisc))]
            """
        );

        await AssertSingleError(
            diagnostics,
            $"The discriminator '{discriminator}' of 'IdDisc' is reserved: '$id', '$ref' and "
                + "'$values' cannot be used as custom type discriminator property names (as in "
                + "System.Text.Json)."
        );

        await Assert.That(ts).Contains("get idDisc(): TypeSpec<IdDisc>");
        await Assert.That(ts).DoesNotContain("polymorphism: {");
    }

    // OPEN-18 (S6c): an interface entry on an interface base. System.Text.Json
    // rejects it too unless FallBackToNearestAncestor is set (measured on
    // .NET 10: "Specified type 'IRound' is not a supported derived type for
    // the polymorphic type 'IRoundBase'. …").
    [Test]
    public async Task InterfaceTypedRegistrationOnAnInterfaceBase_IsReported()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(IRound), "r")]
            [JsonDerivedType(typeof(Disc), "d")]
            public interface IRoundBase { int V { get; } }

            [Transpile]
            public interface IRound : IRoundBase { }

            [Transpile]
            public sealed record Disc(int V) : IRound;

            [Transpile]
            [JsonSerializable(typeof(IRoundBase))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'IRound' registered on 'IRoundBase' is an interface; interface-typed derived types "
                + "are not supported by the TypeScript context."
        );

        await Assert.That(ts).Contains("spec: () => this.disc,");
        await Assert.That(ts).DoesNotContain("get iRound()");
    }

    [Test]
    public async Task TypeNotImplementingTheInterfaceBase_IsNotAssignable()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile]
            [JsonDerivedType(typeof(Sq), "sq")]
            [JsonDerivedType(typeof(Stranger), "x")]
            public interface IShape { double Area { get; } }

            [Transpile]
            public sealed record Sq(double Area) : IShape;

            [Transpile]
            public sealed record Stranger(double Area);

            [Transpile]
            [JsonSerializable(typeof(IShape))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "'Stranger' registered by [JsonDerivedType] on 'IShape' is not assignable to 'IShape'."
        );
    }

    // FR-019 rule 4: the same required members with the same kinds.
    [Test]
    public async Task IndistinguishablePlainObjectVariants_AreReported()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(A), "a")]
            [JsonDerivedType(typeof(B), "b")]
            public abstract record Base;

            [Transpile, PlainObject]
            public sealed record A(string Name) : Base;

            [Transpile, PlainObject]
            public sealed record B(string Name) : Base;

            [Transpile]
            [JsonSerializable(typeof(Base))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "[PlainObject] variants 'A' and 'B' of 'Base' accept each other's values (required "
                + "members: name: string), so serialization cannot tell them apart. Add a "
                + "distinguishing required member or use class-backed records."
        );

        await Assert.That(ts).DoesNotContain("polymorphism: {");
    }

    // A guard that requires nothing accepts every plain object.
    [Test]
    public async Task PlainObjectVariantsWithoutRequiredMembers_AreReported()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(A), "a")]
            [JsonDerivedType(typeof(B), "b")]
            public abstract record Base;

            [Transpile, PlainObject]
            public sealed record A : Base;

            [Transpile, PlainObject]
            public sealed record B(int? Count) : Base;

            [Transpile]
            [JsonSerializable(typeof(Base))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "[PlainObject] variants 'A' and 'B' of 'Base' accept each other's values (required "
                + "members: none), so serialization cannot tell them apart. Add a "
                + "distinguishing required member or use class-backed records."
        );
    }

    // A nullable member is not required, but a value may still carry it:
    // A's guard accepts every B and B's guard accepts an A that has `x`.
    [Test]
    public async Task VariantsThatCoverEachOtherThroughAnOptionalMember_AreReported()
    {
        var (_, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(A), "a")]
            [JsonDerivedType(typeof(B), "b")]
            public abstract record Base;

            [Transpile, PlainObject]
            public sealed record A(string Name, int? X) : Base;

            [Transpile, PlainObject]
            public sealed record B(string Name, int X) : Base;

            [Transpile]
            [JsonSerializable(typeof(Base))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "[PlainObject] variants 'A' and 'B' of 'Base' accept each other's values (required "
                + "members: name: string, x: number), so serialization cannot tell them apart. "
                + "Add a distinguishing required member or use class-backed records."
        );
    }

    // X ⊂ Y ⊂ Z ⊂ X in the "more specific" relation: no order is consistent.
    [Test]
    public async Task CyclicSpecificity_IsReported()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(X), "x")]
            [JsonDerivedType(typeof(Y), "y")]
            [JsonDerivedType(typeof(Z), "z")]
            public abstract record Base;

            [Transpile, PlainObject]
            public sealed record X(string A, string? B) : Base;

            [Transpile, PlainObject]
            public sealed record Y(string B, string? C) : Base;

            [Transpile, PlainObject]
            public sealed record Z(string C, string? A) : Base;

            [Transpile]
            [JsonSerializable(typeof(Base))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "[PlainObject] variants 'X', 'Y' and 'Z' of 'Base' have no consistent test order."
        );

        await Assert.That(ts).DoesNotContain("polymorphism: {");
    }

    // Members of the same name with incompatible kinds tell variants apart.
    [Test]
    public async Task VariantsDistinguishedByMemberKind_AreAccepted()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(A), "a")]
            [JsonDerivedType(typeof(B), "b")]
            public abstract record Base;

            [Transpile, PlainObject]
            public sealed record A(string V) : Base;

            [Transpile, PlainObject]
            public sealed record B(int V) : Base;

            [Transpile]
            [JsonSerializable(typeof(Base))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(ts).Contains("test: isB,");
        await Assert.That(ts).DoesNotContain("testOrder");
    }

    // FR-019 rules 4–5: the variant guards run before the base's baseTest, so
    // a variant whose guard accepts every base value would capture them.
    // System.Text.Json (.NET 10) writes Animal2("a") as {"Name":"a"} and
    // Dog2("d", null) as {"$type":"dog","Bark":null,"Name":"d"}; the
    // TypeScript context would have written {name: "a"} as the latter.
    [Test]
    [Arguments("int? Bark", "name: string")]
    [Arguments("", "name: string")]
    public async Task VariantWhoseGuardAcceptsTheBase_IsReported(
        string extraMember,
        string requiredMembers
    )
    {
        var members = extraMember.Length == 0 ? "string Name" : $"string Name, {extraMember}";

        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            $$"""
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(Dog2), "dog")]
            public record Animal2(string Name);

            [Transpile, PlainObject]
            public sealed record Dog2({{members}}) : Animal2(Name);

            [Transpile]
            [JsonSerializable(typeof(Animal2))]
            """
        );

        await AssertSingleError(
            diagnostics,
            $"[PlainObject] variant 'Dog2' of 'Animal2' accepts every 'Animal2' value (required "
                + $"members: {requiredMembers}), so serialization would write each 'Animal2' as "
                + "'Dog2'. Add a distinguishing required member to 'Dog2' or use class-backed "
                + "records."
        );

        await Assert.That(ts).DoesNotContain("polymorphism: {");
    }

    // A [Discriminator] member is compared to the variant's own literal, so
    // two variants with otherwise identical shapes are told apart by it.
    [Test]
    public async Task DiscriminatorLiterals_TellVariantsApart()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, StringEnum]
            public enum Kind { KA, KB }

            [Transpile, PlainObject]
            [JsonDerivedType(typeof(KA), "a")]
            [JsonDerivedType(typeof(KB), "b")]
            public abstract record KBase;

            [Transpile, PlainObject, Metano.Annotations.TypeScript.Discriminator("Kind")]
            public sealed record KA(Kind Kind, double X) : KBase;

            [Transpile, PlainObject, Metano.Annotations.TypeScript.Discriminator("Kind")]
            public sealed record KB(Kind Kind, double X) : KBase;

            [Transpile]
            [JsonSerializable(typeof(KBase))]
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(ts).Contains("test: isKA,");
        await Assert.That(ts).Contains("test: isKB,");
    }

    // A [PlainObject] type with no TypeScript guard ([Import] here) cannot be
    // selected on write; referencing `isNE` would import a missing module.
    [Test]
    public async Task PlainObjectRegistrationWithoutAGuard_IsReportedAndLeftOut()
    {
        var (ts, diagnostics) = TranspileWithPolymorphismDiagnostics(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(NE), "ne")]
            [JsonDerivedType(typeof(Ok), "ok")]
            public abstract record NB;

            [Transpile, PlainObject, Import("NE", "ext-lib")]
            public sealed record NE(string Ne) : NB;

            [Transpile, PlainObject]
            public sealed record Ok(double V) : NB;

            [Transpile]
            [JsonSerializable(typeof(NB))]
            """
        );

        await AssertSingleError(
            diagnostics,
            "[PlainObject] type 'NE' registered on 'NB' gets no TypeScript guard (for example "
                + "[Import], [ExportedAsModule], [NoContainer], an exception or a type with "
                + "extension members), so serialization cannot select it."
        );

        await Assert.That(ts).Contains("test: isOk,");
        await Assert.That(ts).DoesNotContain("test: isNE");
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
