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
    /// The properties a type's spec declares. A spec with a <c>base</c> link
    /// declares only the type's own properties — the runtime reads the
    /// inherited ones through the base spec. Without a link (the base emits
    /// no spec), the inherited properties of every toolchain-declared ancestor
    /// are inlined ahead of the own ones, base-most first like the runtime's
    /// base walk, so they are still written, read and passed to the factory.
    /// Like System.Text.Json, the inlined list is deduplicated by final JSON
    /// name (FR-009): a property redeclared down the chain (<c>override</c> or
    /// <c>new</c>) under the same JSON name is declared once, at its base-most
    /// position with its most-derived declaration, while a redeclaration under
    /// another JSON name is written under both names.
    /// </summary>
    public List<JsonSerializableProperty> PropertiesOf(INamedTypeSymbol type)
    {
        if (LinkedBaseOf(type) is not null)
            return CollectOwnProperties(type);

        return InlinedAncestorsOf(type)
            .Reverse()
            .Append(type)
            .SelectMany(CollectOwnProperties)
            .GroupBy(property => property.JsonName, StringComparer.Ordinal)
            .Select(redeclarations => redeclarations.Last())
            .ToList();
    }

    /// <summary>
    /// The base type a spec links to through <c>base: this.x</c>, or
    /// <c>null</c> when the base has no object contract in the context. The
    /// base is admitted by the closure rule
    /// (<see cref="JsonContractAdmission.IsDiscoverable"/>), evaluated against
    /// its declaring assembly (FR-010): a base of this project under
    /// <c>[Transpile]</c> or <c>[assembly: TranspileAssembly]</c>, a closed
    /// generic base, and a base of a referenced Metano package (FR-047) all
    /// link. Any other base (<c>[Ignore]</c>, BCL) has its properties inlined
    /// instead (<see cref="PropertiesOf"/>).
    /// </summary>
    public INamedTypeSymbol? LinkedBaseOf(INamedTypeSymbol type) =>
        type.BaseType is { } baseType && _admission.IsDiscoverable(baseType) ? baseType : null;

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

    private static bool IsSerializable(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsImplicitlyDeclared
        && property.DeclaredAccessibility == Accessibility.Public
        && !SymbolHelper.HasAttribute(property, "JsonIgnore")
        && !property.IsIndexer;

    private JsonSerializableProperty ToSerializableProperty(IPropertySymbol property)
    {
        var tsName = IrToTsNamingPolicy.ToDataMemberName(
            property.Name,
            SymbolHelper.GetNameOverride(property, TargetLanguage.TypeScript)
        );

        // JSON name: [JsonPropertyName] wins, otherwise apply naming policy
        var jsonName = JsonPropertyNameOf(property) ?? ApplyNamingPolicy(property.Name);

        return new JsonSerializableProperty(property, jsonName, tsName);
    }

    /// <summary>
    /// Applies the naming policy to a C# property name to get the JSON wire name.
    /// If no policy is set, the original PascalCase name is preserved (System.Text.Json default).
    /// </summary>
    private string ApplyNamingPolicy(string csharpName) =>
        _namingPolicy?.Invoke(csharpName) ?? csharpName;

    /// <summary>
    /// Reads [JsonPropertyName("name")] from a property if present.
    /// </summary>
    private static string? JsonPropertyNameOf(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            if (
                attr.AttributeClass?.Name is not ("JsonPropertyNameAttribute" or "JsonPropertyName")
            )
                continue;

            if (attr.ConstructorArguments.Length > 0)
                return attr.ConstructorArguments[0].Value?.ToString();
        }

        return null;
    }
}

/// <summary>A serializable property with its JSON wire name and TS member
/// name.</summary>
internal sealed record JsonSerializableProperty(
    IPropertySymbol Property,
    string JsonName,
    string TsName
);
