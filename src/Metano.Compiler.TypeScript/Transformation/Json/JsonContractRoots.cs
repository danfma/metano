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
/// </summary>
internal sealed class JsonContractRoots(
    TypeScriptTransformContext context,
    JsonContractAdmission admission
)
{
    private readonly TypeScriptTransformContext _context = context;
    private readonly JsonContractAdmission _admission = admission;

    /// <summary>
    /// The valid roots in attribute order. A type listed twice appears twice;
    /// the closure keeps its first occurrence.
    /// </summary>
    public IReadOnlyList<JsonContractRoot> Read(INamedTypeSymbol contextType)
    {
        var structurallyValid = CandidatesOf(contextType).Where(IsStructurallyValid).ToList();

        var objectRoots = new HashSet<ITypeSymbol>(
            structurallyValid.Where(root => root.Kind == JsonPropertyKind.Ref).Select(x => x.Type),
            SymbolEqualityComparer.Default
        );

        return structurallyValid.Where(root => HasDescribableContents(root, objectRoots)).ToList();
    }

    /// <summary>
    /// Every type a <c>[JsonSerializable]</c> attribute names, unvalidated.
    /// </summary>
    public static IEnumerable<ITypeSymbol> DeclaredTypesOf(INamedTypeSymbol contextType) =>
        CandidatesOf(contextType).Select(root => root.Type);

    private static IEnumerable<JsonContractRoot> CandidatesOf(INamedTypeSymbol contextType) =>
        contextType
            .GetAttributes()
            .Where(IsJsonSerializable)
            .Select(ToCandidate)
            .OfType<JsonContractRoot>();

    private static bool IsJsonSerializable(AttributeData attribute) =>
        attribute.AttributeClass?.Name is "JsonSerializableAttribute" or "JsonSerializable";

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

    private bool IsStructurallyValid(JsonContractRoot root)
    {
        var reason = RejectStructure(root);

        if (reason is null)
            return true;

        Report(root, reason);

        return false;
    }

    /// <summary>
    /// The FR-035 rules that depend only on the root type itself. The
    /// contents of a value root (its elements, keys and values) are checked
    /// by <see cref="HasDescribableContents"/> once every object root is known.
    /// </summary>
    private string? RejectStructure(JsonContractRoot root) =>
        root.Type switch
        {
            { TypeKind: TypeKind.Error } => "the type could not be resolved",
            ITypeParameterSymbol => "a type parameter has no fixed JSON contract",
            INamedTypeSymbol { IsUnboundGenericType: true } =>
                "an open generic type has no fixed JSON contract",
            { SpecialType: SpecialType.System_Object } or IDynamicTypeSymbol =>
                "'object' and 'dynamic' have no fixed JSON contract",
            _ when root.Kind == JsonPropertyKind.Ref => RejectObjectRoot(root.Type),
            _ => null,
        };

    private string? RejectObjectRoot(ITypeSymbol type) =>
        type is INamedTypeSymbol named
            ? _admission.RejectObjectRoot(named)
            : "it has no JSON object contract";

    /// <summary>
    /// A value root is describable when every type its descriptor nests is
    /// too: arrays have rank 1, dictionary keys are strings, and every object
    /// element, value or nullable inner has a spec in the context (it is
    /// discovered by the closure or listed as a root itself). Object roots
    /// pass trivially: their properties follow the property rules.
    /// </summary>
    private bool HasDescribableContents(
        JsonContractRoot root,
        IReadOnlySet<ITypeSymbol> objectRoots
    )
    {
        if (root.Kind == JsonPropertyKind.Ref)
            return true;

        var reason = RejectContents(root.Type, root.Kind, objectRoots);

        if (reason is null)
            return true;

        Report(root, reason);

        return false;
    }

    private string? RejectContents(
        ITypeSymbol type,
        JsonPropertyKind kind,
        IReadOnlySet<ITypeSymbol> objectRoots
    )
    {
        if (type is IArrayTypeSymbol { Rank: > 1 })
            return "multi-dimensional arrays are not supported by System.Text.Json";

        if (kind == JsonPropertyKind.Map && !HasStringKey(type))
            return "dictionary keys other than 'string' are not supported by the TypeScript "
                + "context yet";

        if (kind == JsonPropertyKind.Ref)
            return RejectNestedObject(type, objectRoots);

        return JsonTypeClassifier
            .ChildTypesOf(type, kind)
            .Select(child => RejectContents(child, JsonTypeClassifier.Classify(child), objectRoots))
            .FirstOrDefault(reason => reason is not null);
    }

    private string? RejectNestedObject(ITypeSymbol type, IReadOnlySet<ITypeSymbol> objectRoots) =>
        _admission.IsDiscoverable(type) || objectRoots.Contains(type)
            ? null
            : $"'{type.ToDisplayString()}' has no TypeScript contract in this context "
                + "(only transpiled, non-generic classes and structs of this project are "
                + "discovered; list a referenced package type as its own root)";

    private static bool HasStringKey(ITypeSymbol mapType) =>
        mapType
            is INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_String }, ..] };

    private void Report(JsonContractRoot root, string reason) =>
        _context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.InvalidJsonSerializableRoot,
                $"'{root.Type.ToDisplayString()}' cannot be a [JsonSerializable] root: {reason}.",
                root.Location
            )
        );
}
