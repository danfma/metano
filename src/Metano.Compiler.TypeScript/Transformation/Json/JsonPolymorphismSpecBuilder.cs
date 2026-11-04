using System.Globalization;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the data-only <c>polymorphism</c> member of a polymorphic base's
/// <c>TypeSpec</c> (spec 009 FR-004, contracts/context-codegen.md §1):
/// <code>
/// polymorphism: {
///   discriminator: "kind",
///   abstract: true,
///   typeName: "Shape",
///   derived: [{ tag: "image", spec: () =&gt; this.imageClip }, { tag: "point", spec: () =&gt; this.point, test: isPoint }, …],
///   testOrder: [1, 0],
///   baseTest: isShape,
///   unknownDerivedType: "fallBackToNearestAncestor",
///   ignoreUnrecognizedTags: true,
/// }
/// </code>
/// <c>abstract</c> and the two options appear only when they differ from
/// System.Text.Json's defaults. Each registration keeps attribute order; an
/// abstract registration (allowed under <c>FallBackToNearestAncestor</c>
/// only) is marked <c>abstract: true</c> so a read of its tag fails as in
/// System.Text.Json (DECIDED-12). A base without a constructor (an interface
/// or a <c>[PlainObject]</c> type) carries <c>typeName</c>; a
/// <c>[PlainObject]</c> registration carries its synthesized guard as
/// <c>test</c>, ordered by <c>testOrder</c> when that differs from attribute
/// order; a concrete, non-self-registered <c>[PlainObject]</c> base carries
/// its own guard as <c>baseTest</c> (FR-019 rule 5).
/// </summary>
internal sealed class JsonPolymorphismSpecBuilder(
    JsonTypeDescriptorBuilder descriptors,
    JsonGuardReferences guards
)
{
    private readonly JsonTypeDescriptorBuilder _descriptors = descriptors;
    private readonly JsonGuardReferences _guards = guards;

    public TsObjectProperty Build(JsonPolymorphism polymorphism)
    {
        var registrations = polymorphism
            .Registrations.Select(BuildRegistration)
            .ToList<TsExpression>();

        var members = new[]
        {
            new TsObjectProperty("discriminator", new TsStringLiteral(polymorphism.Discriminator)),
            TrueFlag("abstract", polymorphism.IsAbstractBase),
            TypeNameProperty(polymorphism),
            new TsObjectProperty("derived", new TsArrayLiteral(registrations)),
            TestOrderProperty(polymorphism.TestOrder),
            BaseTestProperty(polymorphism),
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
    /// registration has no <c>tag</c>, a self-registration links to the
    /// base's own getter, and a <c>[PlainObject]</c> registration ends with
    /// its guard (<c>test: isPoint</c>).
    /// </summary>
    private TsObjectLiteral BuildRegistration(JsonDerivedTypeRegistration registration)
    {
        var members = new[]
        {
            registration.Tag is { } tag ? new TsObjectProperty("tag", TagLiteralOf(tag)) : null,
            new TsObjectProperty("spec", _descriptors.BuildSpecLink(registration.Type)),
            TrueFlag("abstract", registration.IsAbstract),
            registration.IsStructural
                ? new TsObjectProperty(
                    "test",
                    _guards.GuardReferenceOf((INamedTypeSymbol)registration.Type)
                )
                : null,
        };

        return new TsObjectLiteral(members.OfType<TsObjectProperty>().ToList());
    }

    /// <summary>
    /// <c>typeName: "IShape"</c>, the C# simple name System.Text.Json's
    /// messages use, for a base the runtime cannot name by its constructor.
    /// </summary>
    private static TsObjectProperty? TypeNameProperty(JsonPolymorphism polymorphism) =>
        polymorphism.HasNoConstructor
            ? new TsObjectProperty(
                "typeName",
                new TsStringLiteral(polymorphism.BaseType.OriginalDefinition.Name)
            )
            : null;

    /// <summary><c>testOrder: [1, 0]</c> when it differs from attribute order.</summary>
    private static TsObjectProperty? TestOrderProperty(IReadOnlyList<int>? testOrder) =>
        testOrder is null
            ? null
            : new TsObjectProperty(
                "testOrder",
                new TsArrayLiteral(
                    testOrder
                        .Select(index => new TsLiteral(
                            index.ToString(CultureInfo.InvariantCulture)
                        ))
                        .ToList<TsExpression>()
                )
            );

    /// <summary><c>baseTest: isShape</c> for the rule 5 base guard.</summary>
    private TsObjectProperty? BaseTestProperty(JsonPolymorphism polymorphism) =>
        polymorphism.NeedsBaseTest
            ? new TsObjectProperty("baseTest", _guards.GuardReferenceOf(polymorphism.BaseType))
            : null;

    /// <summary>
    /// A string tag prints as a string literal, an int tag as a number
    /// literal (FR-014: verbatim).
    /// </summary>
    private static TsExpression TagLiteralOf(JsonTypeTag tag) =>
        tag switch
        {
            JsonTypeTag.Text text => new TsStringLiteral(text.Value),
            JsonTypeTag.Number number => new TsLiteral(number.ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown tag kind."),
        };

    /// <summary>
    /// <c>unknownDerivedType</c> for a non-default handling; absent for
    /// <c>FailSerialization</c>.
    /// </summary>
    private static TsObjectProperty? UnknownDerivedTypeProperty(
        JsonUnknownDerivedTypeHandling handling
    ) =>
        WireNameOf(handling) is { } wireName
            ? new TsObjectProperty("unknownDerivedType", new TsStringLiteral(wireName))
            : null;

    /// <summary>
    /// The runtime <c>UnknownDerivedTypeHandling</c> value of a non-default
    /// handling.
    /// </summary>
    private static string? WireNameOf(JsonUnknownDerivedTypeHandling handling) =>
        handling switch
        {
            JsonUnknownDerivedTypeHandling.FallBackToBaseType => "fallBackToBaseType",
            JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor => "fallBackToNearestAncestor",
            _ => null,
        };

    private static TsObjectProperty? TrueFlag(string name, bool isSet) =>
        isSet ? new TsObjectProperty(name, new TsLiteral("true")) : null;
}
