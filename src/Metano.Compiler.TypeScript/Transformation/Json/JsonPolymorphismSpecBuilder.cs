using System.Globalization;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the data-only <c>polymorphism</c> member of a polymorphic base's
/// <c>TypeSpec</c> (spec 009 FR-004, contracts/context-codegen.md §1):
/// <code>
/// polymorphism: {
///   discriminator: "kind",
///   abstract: true,
///   derived: [{ tag: "image", spec: () =&gt; this.imageClip }, …],
///   unknownDerivedType: "fallBackToNearestAncestor",
///   ignoreUnrecognizedTags: true,
/// }
/// </code>
/// <c>abstract</c> and the two options appear only when they differ from
/// System.Text.Json's defaults. Each registration keeps attribute order; an
/// abstract registration (allowed under <c>FallBackToNearestAncestor</c>
/// only) is marked <c>abstract: true</c> so a read of its tag fails as in
/// System.Text.Json (DECIDED-12).
/// </summary>
internal sealed class JsonPolymorphismSpecBuilder(JsonTypeDescriptorBuilder descriptors)
{
    private readonly JsonTypeDescriptorBuilder _descriptors = descriptors;

    public TsObjectProperty Build(JsonPolymorphism polymorphism)
    {
        var derived = polymorphism.Derived.Select(BuildRegistration).ToList<TsExpression>();

        var members = new[]
        {
            new TsObjectProperty("discriminator", new TsStringLiteral(polymorphism.Discriminator)),
            TrueFlag("abstract", polymorphism.IsAbstractBase),
            new TsObjectProperty("derived", new TsArrayLiteral(derived)),
            UnknownDerivedTypeProperty(polymorphism.UnknownDerivedType),
            TrueFlag("ignoreUnrecognizedTags", polymorphism.IgnoreUnrecognizedTags),
        };

        return new TsObjectProperty(
            "polymorphism",
            new TsObjectLiteral(members.OfType<TsObjectProperty>().ToList())
        );
    }

    /// <summary>
    /// <c>{ tag: "image", spec: () =&gt; this.imageClip }</c>; an untagged
    /// registration has no <c>tag</c>, and a self-registration links to the
    /// base's own getter.
    /// </summary>
    private TsObjectLiteral BuildRegistration(JsonDerivedType registration)
    {
        var members = new[]
        {
            registration.Tag is { } tag ? new TsObjectProperty("tag", TagLiteralOf(tag)) : null,
            new TsObjectProperty("spec", _descriptors.BuildSpecLink(registration.Type)),
            TrueFlag("abstract", registration.IsAbstract),
        };

        return new TsObjectLiteral(members.OfType<TsObjectProperty>().ToList());
    }

    /// <summary>A string tag prints as a string literal, an int tag as a
    /// number literal (FR-014: verbatim).</summary>
    private static TsExpression TagLiteralOf(JsonTypeTag tag) =>
        tag switch
        {
            JsonTypeTag.Text text => new TsStringLiteral(text.Value),
            JsonTypeTag.Number number => new TsLiteral(
                number.Value.ToString(CultureInfo.InvariantCulture)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown tag kind."),
        };

    private static TsObjectProperty? UnknownDerivedTypeProperty(
        JsonUnknownDerivedTypeHandling handling
    ) =>
        handling switch
        {
            JsonUnknownDerivedTypeHandling.FallBackToBaseType => new TsObjectProperty(
                "unknownDerivedType",
                new TsStringLiteral("fallBackToBaseType")
            ),
            JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor => new TsObjectProperty(
                "unknownDerivedType",
                new TsStringLiteral("fallBackToNearestAncestor")
            ),
            _ => null,
        };

    private static TsObjectProperty? TrueFlag(string name, bool isSet) =>
        isSet ? new TsObjectProperty(name, new TsLiteral("true")) : null;
}
