using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The serializable shape of an object contract: the properties its spec
/// declares, with their JSON wire names, and the base spec it links to.
/// </summary>
internal sealed class JsonPropertyCollector(
    TypeScriptTransformContext context,
    JsonContractAdmission admission,
    Func<string, string>? namingPolicy
)
{
    private readonly TypeScriptTransformContext _context = context;
    private readonly JsonContractAdmission _admission = admission;
    private readonly Func<string, string>? _namingPolicy = namingPolicy;

    /// <summary>
    /// The properties a type's spec declares, in declaration order: the
    /// order the closure walks them and the factory passes them. A spec with
    /// a <c>base</c> link declares only the type's own properties — the
    /// runtime reads the inherited ones through the base spec. Without a link
    /// (the base emits no spec), the inherited properties of every
    /// toolchain-declared ancestor are inlined ahead of the own ones,
    /// base-most first, so they are still written, read and passed to the
    /// factory. The inlined list follows System.Text.Json's hierarchy rules
    /// (FR-009): a redeclaration (<c>override</c> or <c>new</c>, the same CLR
    /// name) under the same JSON name is declared once, at its base-most
    /// position with its most-derived declaration; a redeclaration under
    /// another JSON name is written under both names; and a
    /// <c>[JsonIgnore]</c> override hides every base declaration of that
    /// name. Distinct properties that share a JSON name (System.Text.Json
    /// throws) are all kept.
    /// </summary>
    public List<JsonSerializableProperty> DeclarationOrderPropertiesOf(INamedTypeSymbol type)
    {
        if (LinkedBaseOf(type) is not null)
            return CollectOwnProperties(type);

        return VisibleDeclarationsByType(type)
            .Reverse()
            .SelectMany(declarations => declarations)
            .GroupBy(RedeclarationKey)
            .Select(redeclarations => redeclarations.Last())
            .ToList();
    }

    /// <summary>
    /// The same properties as <see cref="DeclarationOrderPropertiesOf"/> in System.Text.Json's
    /// wire order, which the emitted <c>properties</c> list follows (FR-009,
    /// DECIDED-19 f): the type's own declarations first, then each inlined
    /// ancestor's, nearest first, with a redeclaration under the same JSON
    /// name at its most-derived position (measured on .NET 10:
    /// <c>{"Y":3,"Name":"d","X":2,"Base":1}</c> when the most-derived type
    /// declares <c>Y</c> and overrides <c>Name</c>). A linked spec's own list
    /// is already in that order; the runtime's <c>collectWireProperties</c>
    /// appends the base spec's.
    /// </summary>
    public List<JsonSerializableProperty> WirePropertiesOf(INamedTypeSymbol type)
    {
        if (LinkedBaseOf(type) is not null)
            return CollectOwnProperties(type);

        return VisibleDeclarationsByType(type)
            .SelectMany(declarations => declarations)
            .GroupBy(RedeclarationKey)
            .Select(redeclarations => redeclarations.First())
            .ToList();
    }

    /// <summary>
    /// Every property the contract of <paramref name="type"/> declares: its
    /// own spec's (<see cref="DeclarationOrderPropertiesOf"/>) and those of
    /// each spec of its <c>base</c> chain.
    /// </summary>
    public IEnumerable<JsonSerializableProperty> ContractPropertiesOf(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = LinkedBaseOf(current))
        {
            foreach (var property in DeclarationOrderPropertiesOf(current))
                yield return property;
        }
    }

    /// <summary>
    /// Every property the object literal of a <c>[PlainObject]</c> factory
    /// sets: the declarations of each spec of the <c>base</c> chain, base-most
    /// first, each TS member once at its most-derived declaration. A linked
    /// spec reads its inherited properties through its base spec, and the
    /// literal must still carry them (FR-019: a <c>[PlainObject]</c> variant
    /// read through a polymorphic base keeps its inherited members).
    /// </summary>
    public List<JsonSerializableProperty> LiteralPropertiesOf(INamedTypeSymbol type)
    {
        var mostDerivedFirst = ContractPropertiesOf(type)
            .DistinctBy(property => property.TsName)
            .ToList();

        var levels = new List<INamedTypeSymbol>();

        for (var current = type; current is not null; current = LinkedBaseOf(current))
            levels.Insert(0, current);

        return levels
            .SelectMany(DeclarationOrderPropertiesOf)
            .Select(property => property.TsName)
            .Distinct()
            .Select(tsName => mostDerivedFirst.First(property => property.TsName == tsName))
            .ToList();
    }

    /// <summary>
    /// The TS names of the base declarations a linked spec hides with a
    /// <c>[JsonIgnore]</c> override (DECIDED-19 e). System.Text.Json writes
    /// and reads none of them; the runtime's <c>collectWireProperties</c>
    /// skips every base declaration whose <c>ts</c> name a more-derived spec
    /// lists in <c>ignoredProperties</c>. Empty for an unlinked spec, whose
    /// inlined list already leaves those declarations out
    /// (<see cref="DeclarationOrderPropertiesOf"/>).
    /// </summary>
    public List<string> IgnoredBasePropertiesOf(INamedTypeSymbol type)
    {
        if (LinkedBaseOf(type) is null)
            return [];

        return type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(IsIgnoredOverride)
            .SelectMany(OverriddenDeclarationsOf)
            .Select(TsNameOf)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// The base type a spec links to through <c>base: this.x</c>, or
    /// <c>null</c> when the base has no object contract in the context. The
    /// base is admitted by the closure rule
    /// (<see cref="JsonContractAdmission.HasObjectContract"/>), evaluated against
    /// its declaring assembly (FR-010): a base of this project under
    /// <c>[Transpile]</c> or <c>[assembly: TranspileAssembly]</c>, a closed
    /// generic base, and a base of a referenced Metano package (FR-047) all
    /// link. Any other base (<c>[Ignore]</c>, BCL) has its properties inlined
    /// instead (<see cref="DeclarationOrderPropertiesOf"/>).
    /// </summary>
    public INamedTypeSymbol? LinkedBaseOf(INamedTypeSymbol type) =>
        type.BaseType is { } baseType && _admission.HasObjectContract(baseType) ? baseType : null;

    /// <summary>
    /// The ancestors whose properties an unlinked spec inlines, nearest first:
    /// the base chain up to <c>object</c> or the first type the toolchain does
    /// not declare (<see cref="SymbolHelper.IsToolchainDeclared"/>: a BCL,
    /// foreign-package, <c>[External]</c> or <c>[Import]</c> base).
    /// </summary>
    private IEnumerable<INamedTypeSymbol> InlinedAncestorsOf(INamedTypeSymbol type)
    {
        for (var ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (!SymbolHelper.IsToolchainDeclared(ancestor, _context.CurrentAssembly))
                yield break;

            yield return ancestor;
        }
    }

    /// <summary>
    /// Collects public properties from the type, excluding [JsonIgnore], and computes
    /// both the TS field name and the JSON wire name.
    /// </summary>
    private List<JsonSerializableProperty> CollectOwnProperties(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(IsSerializable)
            .Select(ToSerializableProperty)
            .ToList();

    /// <summary>
    /// The visible own declarations of an unlinked spec's type and of each
    /// inlined ancestor, one list per type, most-derived first. A
    /// declaration is hidden when a more-derived type of the chain overrides
    /// it with <c>[JsonIgnore]</c>. An interface (FR-018) has no base class;
    /// its chain is the interface and every interface it extends, in
    /// System.Text.Json's order (<see cref="JsonInterfaceHierarchy"/>).
    /// </summary>
    private IReadOnlyList<List<JsonSerializableProperty>> VisibleDeclarationsByType(
        INamedTypeSymbol type
    )
    {
        var chain =
            type.TypeKind == TypeKind.Interface
                ? JsonInterfaceHierarchy.Of(type)
                : InlinedAncestorsOf(type).Prepend(type).ToList();

        return chain
            .Select(
                (declaringType, depth) =>
                    VisibleOwnDeclarationsOf(declaringType, derivedTypes: chain.Take(depth))
            )
            .ToList();
    }

    /// <summary>
    /// The own declarations of <paramref name="declaringType"/> that none of
    /// its <paramref name="derivedTypes"/> hides with a <c>[JsonIgnore]</c>
    /// override.
    /// </summary>
    private List<JsonSerializableProperty> VisibleOwnDeclarationsOf(
        INamedTypeSymbol declaringType,
        IEnumerable<INamedTypeSymbol> derivedTypes
    ) =>
        CollectOwnProperties(declaringType)
            .Where(property => !IsHiddenByIgnoredOverride(derivedTypes, property.Property.Name))
            .ToList();

    private static (string ClrName, string JsonName) RedeclarationKey(
        JsonSerializableProperty property
    ) => (property.Property.Name, property.JsonName);

    /// <summary>
    /// Whether one of the <paramref name="derivedTypes"/> overrides the
    /// property with <c>[JsonIgnore]</c>, which hides the base declarations
    /// as in System.Text.Json. Only an override counts: STJ hides a base
    /// declaration under a non-virtual <c>new</c> redeclaration only when
    /// their JSON names collide.
    /// </summary>
    private static bool IsHiddenByIgnoredOverride(
        IEnumerable<INamedTypeSymbol> derivedTypes,
        string propertyName
    ) =>
        derivedTypes
            .SelectMany(derived => derived.GetMembers(propertyName).OfType<IPropertySymbol>())
            .Any(IsIgnoredOverride);

    private static bool IsIgnoredOverride(IPropertySymbol property) =>
        property.IsOverride && SymbolHelper.HasAttribute(property, "JsonIgnore");

    private static IEnumerable<IPropertySymbol> OverriddenDeclarationsOf(IPropertySymbol property)
    {
        for (
            var overridden = property.OverriddenProperty;
            overridden is not null;
            overridden = overridden.OverriddenProperty
        )
        {
            yield return overridden;
        }
    }

    private static bool IsSerializable(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsImplicitlyDeclared
        && property.DeclaredAccessibility == Accessibility.Public
        && !SymbolHelper.HasAttribute(property, "JsonIgnore")
        && !property.IsIndexer;

    private JsonSerializableProperty ToSerializableProperty(IPropertySymbol property)
    {
        // JSON name: [JsonPropertyName] wins, otherwise apply naming policy
        var jsonName = JsonPropertyNameOf(property) ?? ApplyNamingPolicy(property.Name);

        return new JsonSerializableProperty(property, jsonName, TsNameOf(property));
    }

    private static string TsNameOf(IPropertySymbol property) =>
        IrToTsNamingPolicy.ToDataMemberName(
            property.Name,
            SymbolHelper.GetNameOverride(property, TargetLanguage.TypeScript)
        );

    /// <summary>
    /// Applies the naming policy to a C# property name to get the JSON wire name.
    /// If no policy is set, the original PascalCase name is preserved (System.Text.Json default).
    /// </summary>
    private string ApplyNamingPolicy(string csharpName) =>
        _namingPolicy?.Invoke(csharpName) ?? csharpName;

    /// <summary>
    /// The name of System.Text.Json's <c>[JsonPropertyName("name")]</c> on
    /// <paramref name="property"/>, or <c>null</c> when it has none.
    /// </summary>
    private static string? JsonPropertyNameOf(IPropertySymbol property) =>
        property
            .GetAttributes()
            .Where(attribute =>
                JsonSerializationAttributes.Is(attribute, "JsonPropertyNameAttribute")
            )
            .Select(attribute =>
                attribute.ConstructorArguments is [{ Value: string name }] ? name : null
            )
            .FirstOrDefault();
}

/// <summary>A serializable property with its JSON wire name and TS member
/// name.</summary>
internal sealed record JsonSerializableProperty(
    IPropertySymbol Property,
    string JsonName,
    string TsName
);
