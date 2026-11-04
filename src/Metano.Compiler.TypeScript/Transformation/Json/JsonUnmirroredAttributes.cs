using System.Text.Json.Serialization;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>MS0037</c> warnings of the System.Text.Json attributes the
/// generated context does not mirror (spec 009 FR-046, DECIDED-25), on the
/// members and types the context's entries reach. Mirrored and therefore
/// not reported: <c>[JsonPropertyName]</c>, <c>[JsonPropertyOrder]</c>,
/// <c>[JsonInclude]</c> on a reachable member, <c>[JsonIgnore]</c> with the
/// <c>Always</c> or <c>Never</c> condition, <c>[JsonRequired]</c> on a
/// non-nullable member (the runtime already rejects a payload without it,
/// FR-045), <c>[JsonConstructor]</c> on the constructor the factory calls,
/// <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c> (FR-004) and
/// <c>[JsonConverter]</c> on a <c>[Branded]</c> / <c>[InlineWrapper]</c> type,
/// whose descriptor writes the wrapped value as such converters do.
/// <c>[JsonStringEnumMemberName]</c> only matters under a string enum
/// converter, which is itself reported.
/// </summary>
internal sealed class JsonUnmirroredAttributes(JsonPropertyCollector properties)
{
    private readonly JsonPropertyCollector _properties = properties;

    /// <summary>
    /// The warnings of every entry, each member and type reported once
    /// (generic constructions share their definition's warnings).
    /// </summary>
    public IEnumerable<MetanoDiagnostic> WarningsOf(IReadOnlyList<JsonContractEntry> entries)
    {
        var contracts = entries
            .Where(entry => entry.Kind == JsonPropertyKind.Ref)
            .Select(entry => entry.Type)
            .OfType<INamedTypeSymbol>()
            .ToList();

        var declaringTypes = contracts
            .SelectMany(_properties.DeclaringTypesOf)
            .Select(type => type.OriginalDefinition)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();

        var memberWarnings = declaringTypes.SelectMany(DataMembersOf).SelectMany(MemberWarningsOf);
        var contractWarnings = declaringTypes.SelectMany(ContractTypeWarningsOf);

        var converterWarnings = ReachedTypesOf(entries, contracts)
            .OfType<INamedTypeSymbol>()
            .Select(type => type.OriginalDefinition)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .Select(TypeConverterWarningOf)
            .OfType<MetanoDiagnostic>();

        return memberWarnings.Concat(contractWarnings).Concat(converterWarnings);
    }

    private static IEnumerable<ISymbol> DataMembersOf(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(member => member is IPropertySymbol { IsIndexer: false } or IFieldSymbol)
            .Where(member => !member.IsStatic && !member.IsImplicitlyDeclared);

    /// <summary>
    /// The warnings of one data member: a <c>[JsonInclude]</c> the source
    /// generator cannot reach, and, on a member System.Text.Json serializes,
    /// every unmirrored member attribute.
    /// </summary>
    private static IEnumerable<MetanoDiagnostic> MemberWarningsOf(ISymbol member)
    {
        var name = UnmirroredJsonWarning.MemberName(member);

        if (JsonMemberAccess.HasUnreachableInclude(member))
        {
            yield return UnmirroredJsonWarning.Create(
                $"'{name}' has [JsonInclude], but the System.Text.Json source generator cannot "
                    + "access it (SYSLIB1038), so its contract throws InvalidOperationException; "
                    + "the TypeScript context does not mirror that.",
                UnmirroredJsonWarning.At(member)
            );
        }

        if (!JsonMemberAccess.IsSerialized(member))
            yield break;

        foreach (var warning in SerializedMemberWarningsOf(member, name))
            yield return warning;
    }

    private static IEnumerable<MetanoDiagnostic> SerializedMemberWarningsOf(
        ISymbol member,
        string name
    ) =>
        new[]
        {
            AttributeWarningOf(
                member,
                JsonSerializationAttributes.ExtensionData,
                $"'{name}' is [JsonExtensionData]: System.Text.Json writes its entries as "
                    + "properties of the object and collects unmapped ones into it, the "
                    + "TypeScript context leaves it out."
            ),
            AttributeWarningOf(
                member,
                JsonSerializationAttributes.Converter,
                $"'{name}' has [JsonConverter]: the TypeScript context converts it as its type, "
                    + "without the custom converter."
            ),
            AttributeWarningOf(
                member,
                JsonSerializationAttributes.NumberHandling,
                $"'{name}' has [JsonNumberHandling]: the TypeScript context applies the "
                    + "context's number handling."
            ),
            PopulateWarningOf(member, name),
            RequiredWarningOf(member, name),
            IgnoreConditionWarningOf(member, name),
        }.OfType<MetanoDiagnostic>();

    /// <summary>The warning at <paramref name="attributeName"/> when <paramref name="symbol"/> carries it.</summary>
    private static MetanoDiagnostic? AttributeWarningOf(
        ISymbol symbol,
        string attributeName,
        string message
    ) =>
        JsonSerializationAttributes.Find(symbol, attributeName) is { } attribute
            ? UnmirroredJsonWarning.Create(message, UnmirroredJsonWarning.At(attribute))
            : null;

    /// <summary>
    /// <c>[JsonObjectCreationHandling(Populate)]</c> fills the existing value
    /// on read; <c>Replace</c> is the default and mirrored.
    /// </summary>
    private static MetanoDiagnostic? PopulateWarningOf(ISymbol symbol, string name)
    {
        var isPopulate =
            JsonSerializationAttributes.Find(
                symbol,
                JsonSerializationAttributes.ObjectCreationHandling
            )
                is { } attribute
            && JsonSerializationAttributes.EnumArgumentOf(attribute)
                == (int)JsonObjectCreationHandling.Populate;

        if (!isPopulate)
            return null;

        return AttributeWarningOf(
            symbol,
            JsonSerializationAttributes.ObjectCreationHandling,
            $"'{name}' has [JsonObjectCreationHandling(Populate)]: the TypeScript context "
                + "replaces the value on read."
        );
    }

    /// <summary>
    /// <c>[JsonRequired]</c> on a nullable member: System.Text.Json rejects a
    /// payload without it, the runtime reads it as <c>null</c>. A
    /// non-nullable member is already required by the runtime (FR-045).
    /// </summary>
    private static MetanoDiagnostic? RequiredWarningOf(ISymbol member, string name)
    {
        if (!JsonTypeClassifier.IsNullable(JsonMemberAccess.TypeOf(member)))
            return null;

        return AttributeWarningOf(
            member,
            JsonSerializationAttributes.Required,
            $"'{name}' has [JsonRequired]: System.Text.Json rejects a payload without it, the "
                + "TypeScript context reads a missing nullable member as null."
        );
    }

    /// <summary>
    /// <c>[JsonIgnore]</c> with a conditional <c>Condition</c>
    /// (<c>WhenWritingNull</c>, <c>WhenWritingDefault</c>, <c>WhenWriting</c>,
    /// <c>WhenReading</c>): the context writes and reads the member always.
    /// </summary>
    private static MetanoDiagnostic? IgnoreConditionWarningOf(ISymbol member, string name)
    {
        var condition = JsonSerializationAttributes.IgnoreConditionOf(member);

        if (condition is null or JsonIgnoreCondition.Always or JsonIgnoreCondition.Never)
            return null;

        return UnmirroredJsonWarning.Create(
            $"'{name}' has [JsonIgnore(Condition = {condition})]: the TypeScript context always "
                + "writes and reads it.",
            UnmirroredJsonWarning.At(member)
        );
    }

    /// <summary>
    /// The type-level attributes of a contract type that change how its
    /// members are written or read.
    /// </summary>
    private static IEnumerable<MetanoDiagnostic> ContractTypeWarningsOf(INamedTypeSymbol type)
    {
        var name = UnmirroredJsonWarning.TypeName(type);

        return new[]
        {
            AttributeWarningOf(
                type,
                JsonSerializationAttributes.NumberHandling,
                $"'{name}' has [JsonNumberHandling]: the TypeScript context applies the "
                    + "context's number handling to its members."
            ),
            DisallowedUnmappedMembersWarningOf(type, name),
            PopulateWarningOf(type, name),
            UncalledJsonConstructorWarningOf(type, name),
        }.OfType<MetanoDiagnostic>();
    }

    /// <summary>
    /// <c>[JsonUnmappedMemberHandling(Disallow)]</c> rejects unknown JSON
    /// properties; <c>Skip</c> is the default and mirrored.
    /// </summary>
    private static MetanoDiagnostic? DisallowedUnmappedMembersWarningOf(
        INamedTypeSymbol type,
        string name
    )
    {
        var isDisallow =
            JsonSerializationAttributes.Find(
                type,
                JsonSerializationAttributes.UnmappedMemberHandling
            )
                is { } attribute
            && JsonSerializationAttributes.EnumArgumentOf(attribute)
                == (int)JsonUnmappedMemberHandling.Disallow;

        if (!isDisallow)
            return null;

        return AttributeWarningOf(
            type,
            JsonSerializationAttributes.UnmappedMemberHandling,
            $"'{name}' has [JsonUnmappedMemberHandling(Disallow)]: the TypeScript context "
                + "skips unmapped JSON properties."
        );
    }

    private static MetanoDiagnostic? UncalledJsonConstructorWarningOf(
        INamedTypeSymbol type,
        string name
    ) =>
        UncalledJsonConstructorOf(type) is { } constructor
            ? UnmirroredJsonWarning.Create(
                $"'{name}' has [JsonConstructor] on a constructor the TypeScript context does "
                    + "not call: it constructs the type through its primary constructor.",
                UnmirroredJsonWarning.At(constructor)
            )
            : null;

    /// <summary>
    /// The <c>[JsonConstructor]</c> constructor of <paramref name="type"/> when
    /// it is not the one the factory calls
    /// (<see cref="IrConstructorExtractor.FindPrimaryConstructor"/>).
    /// </summary>
    private static IMethodSymbol? UncalledJsonConstructorOf(INamedTypeSymbol type)
    {
        var marked = type.InstanceConstructors.FirstOrDefault(constructor =>
            JsonSerializationAttributes.Has(constructor, JsonSerializationAttributes.Constructor)
        );

        if (marked is null)
            return null;

        var called = IrConstructorExtractor.FindPrimaryConstructor(type);

        return SymbolEqualityComparer.Default.Equals(marked, called) ? null : marked;
    }

    /// <summary>
    /// <c>[JsonConverter]</c> on a type the context reaches, except a branded
    /// type (see the class summary).
    /// </summary>
    private static MetanoDiagnostic? TypeConverterWarningOf(INamedTypeSymbol type) =>
        SymbolHelper.HasBranded(type)
            ? null
            : AttributeWarningOf(
                type,
                JsonSerializationAttributes.Converter,
                $"'{UnmirroredJsonWarning.TypeName(type)}' has [JsonConverter]: the TypeScript "
                    + "context converts it with its built-in descriptor, without the custom "
                    + "converter."
            );

    /// <summary>
    /// Every type a descriptor of the context names: the entries, and each
    /// type nested in a serialized member's or value root's descriptor.
    /// </summary>
    private IEnumerable<ITypeSymbol> ReachedTypesOf(
        IReadOnlyList<JsonContractEntry> entries,
        IReadOnlyList<INamedTypeSymbol> contracts
    )
    {
        var memberTypes = contracts
            .SelectMany(_properties.DeclarationOrderPropertiesOf)
            .Select(property => property.Type);

        return entries.Select(entry => entry.Type).Concat(memberTypes).SelectMany(NestedTypesOf);
    }

    private static IEnumerable<ITypeSymbol> NestedTypesOf(ITypeSymbol type) =>
        JsonTypeClassifier
            .ChildTypesOf(type, JsonTypeClassifier.Classify(type))
            .SelectMany(NestedTypesOf)
            .Prepend(type);
}
