using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// One <c>[JsonSerializable]</c> application: the root type, its optional
/// <c>TypeInfoPropertyName</c>, and where the attribute is written (for
/// diagnostics).
/// </summary>
internal sealed record JsonContractRoot(
    ITypeSymbol Type,
    string? TypeInfoPropertyName,
    Location? Location
)
{
    public JsonPropertyKind Kind { get; } = JsonTypeClassifier.Classify(Type);
}

/// <summary>
/// Reads the <c>[JsonSerializable]</c> roots of a context (FR-030) and
/// rejects, with <c>MS0035</c>, every root the TypeScript context cannot
/// describe (FR-035) instead of dropping it or emitting an invalid spec.
/// Diagnostics go to <paramref name="report"/>, so a caller that only needs
/// the admitted roots can read them silently.
/// </summary>
internal sealed class JsonContractRoots(
    JsonContractAdmission admission,
    Action<MetanoDiagnostic> report
)
{
    private readonly JsonContractAdmission _admission = admission;
    private readonly Action<MetanoDiagnostic> _report = report;

    /// <summary>
    /// The admitted roots in attribute order. A type listed twice appears
    /// twice (its <c>TypeInfoPropertyName</c> values are compared by
    /// <see cref="JsonTypeInfoNames"/>); the closure keeps its first
    /// occurrence. A rejected type is reported once, at its first attribute.
    /// </summary>
    public IReadOnlyList<JsonContractRoot> Read(INamedTypeSymbol contextType)
    {
        var candidates = CandidatesOf(contextType).ToList();

        var distinctRoots = candidates
            .DistinctBy(root => root.Type, SymbolEqualityComparer.Default)
            .ToList();

        var admittedTypes = new HashSet<ITypeSymbol>(
            AdmitReportingRejections(distinctRoots).Select(root => root.Type),
            SymbolEqualityComparer.Default
        );

        return candidates.Where(root => admittedTypes.Contains(root.Type)).ToList();
    }

    private static IEnumerable<JsonContractRoot> CandidatesOf(INamedTypeSymbol contextType) =>
        contextType
            .GetAttributes()
            .Where(IsJsonSerializable)
            .Select(ToCandidate)
            .OfType<JsonContractRoot>();

    private static bool IsJsonSerializable(AttributeData attribute) =>
        attribute.AttributeClass?.Name is "JsonSerializableAttribute" or "JsonSerializable";

    private static bool IsObjectRoot(JsonContractRoot root) => root.Kind == JsonPropertyKind.Ref;

    private static JsonContractRoot? ToCandidate(AttributeData attribute)
    {
        if (attribute.ConstructorArguments is not [{ Value: ITypeSymbol type }, ..])
            return null;

        var typeInfoPropertyName = attribute
            .NamedArguments.Where(argument => argument.Key == "TypeInfoPropertyName")
            .Select(argument => argument.Value.Value as string)
            .FirstOrDefault(name => !string.IsNullOrEmpty(name));

        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

        return new JsonContractRoot(type, typeInfoPropertyName, location);
    }

    /// <summary>
    /// Reports <c>MS0035</c> for every root that has a rejection reason and
    /// returns the others, in order.
    /// </summary>
    private List<JsonContractRoot> AdmitReportingRejections(IEnumerable<JsonContractRoot> roots)
    {
        var admitted = new List<JsonContractRoot>();

        foreach (var root in roots)
        {
            if (RejectionReasonOf(root) is { } reason)
            {
                Report(root, reason);
                continue;
            }

            admitted.Add(root);
        }

        return admitted;
    }

    private string? RejectionReasonOf(JsonContractRoot root) =>
        StructureRejectionReasonOf(root) ?? ContentsRejectionReasonOf(root);

    /// <summary>
    /// The FR-035 rules that depend only on the root type itself. The
    /// contents of a value root (its elements, keys and values) are checked
    /// by <see cref="ContentsRejectionReasonOf"/>.
    /// </summary>
    private string? StructureRejectionReasonOf(JsonContractRoot root) =>
        root.Type switch
        {
            { TypeKind: TypeKind.Error } => "the type could not be resolved",
            ITypeParameterSymbol => "a type parameter has no fixed JSON contract",
            INamedTypeSymbol { IsUnboundGenericType: true } =>
                "an open generic type has no fixed JSON contract",
            { SpecialType: SpecialType.System_Object } or IDynamicTypeSymbol =>
                "'object' and 'dynamic' have no fixed JSON contract",
            INamedTypeSymbol named when IsObjectRoot(root) =>
                _admission.ObjectRootRejectionReasonOf(named),
            _ when IsObjectRoot(root) => "it has no JSON object contract",
            _ => null,
        };

    /// <summary>
    /// Why a root's contents are not describable, or <c>null</c> when every
    /// type its descriptor nests is: arrays have rank 1, dictionary keys are
    /// strings, and every object element, value, nullable inner or type
    /// argument is an object contract the closure discovers. A closed generic
    /// object root is checked through its type arguments, the types its
    /// properties take from the root (<c>Page&lt;System.Version&gt;</c> would
    /// otherwise emit a <c>ref</c> to a missing getter); its other properties
    /// follow the property rules.
    /// </summary>
    private string? ContentsRejectionReasonOf(JsonContractRoot root) =>
        IsObjectRoot(root)
            ? TypeArgumentsRejectionReasonOf(root.Type)
            : NestedRejectionReasonOf(root.Type, root.Kind);

    private string? NestedRejectionReasonOf(ITypeSymbol type, JsonPropertyKind kind)
    {
        if (type is IArrayTypeSymbol { Rank: > 1 })
            return "multi-dimensional arrays are not supported by System.Text.Json";

        if (kind == JsonPropertyKind.Map && !HasStringKey(type))
            return "dictionary keys other than 'string' are not supported by the TypeScript "
                + "context yet";

        if (kind == JsonPropertyKind.Ref)
            return NestedObjectRejectionReasonOf(type);

        return JsonTypeClassifier
            .ChildTypesOf(type, kind)
            .Select(child => NestedRejectionReasonOf(child, JsonTypeClassifier.Classify(child)))
            .FirstOrDefault(reason => reason is not null);
    }

    private string? NestedObjectRejectionReasonOf(ITypeSymbol type) =>
        _admission.HasObjectContract(type)
            ? TypeArgumentsRejectionReasonOf(type)
            : $"'{type.ToDisplayString()}' has no TypeScript contract in this context "
                + "(it is not a transpiled class or struct of this project or of a referenced "
                + "Metano package)";

    /// <summary>
    /// The first nested rejection among the type arguments of a closed
    /// generic object contract, or <c>null</c>. Every type argument is
    /// checked, used by a property or not: the spec's TS type names them all
    /// (<c>TypeSpec&lt;Tagged&lt;Version&gt;&gt;</c>).
    /// </summary>
    private string? TypeArgumentsRejectionReasonOf(ITypeSymbol type) =>
        type is INamedTypeSymbol named
            ? named
                .TypeArguments.Select(argument =>
                    NestedRejectionReasonOf(argument, JsonTypeClassifier.Classify(argument))
                )
                .FirstOrDefault(reason => reason is not null)
            : null;

    private static bool HasStringKey(ITypeSymbol mapType) =>
        JsonTypeClassifier.KeyTypeOf(mapType).SpecialType == SpecialType.System_String;

    private void Report(JsonContractRoot root, string reason) =>
        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.InvalidJsonSerializableRoot,
                $"'{root.Type.ToDisplayString()}' cannot be a [JsonSerializable] root: {reason}.",
                root.Location
            )
        );
}
