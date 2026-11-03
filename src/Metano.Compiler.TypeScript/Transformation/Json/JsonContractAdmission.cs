using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Decides which object types the generated context can describe with a
/// <c>TypeSpec</c>. The same rule admits a <c>[JsonSerializable]</c> object
/// root, a type the closure reaches through a property or a value root, and
/// the target of a <c>base</c> link.
/// </summary>
internal sealed class JsonContractAdmission(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    /// <summary>
    /// An object contract: a class or struct (closed generics included,
    /// FR-033) that is transpiled in its declaring assembly (FR-010) — this
    /// project, or a referenced <c>[TranspileAssembly]</c> +
    /// <c>[EmitPackage]</c> library whose types the context imports (FR-047).
    /// Interfaces need polymorphic JSON and are not admitted.
    /// </summary>
    public bool HasObjectContract(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && IsUserClassOrStruct(named)
        && IsTranspiledInDeclaringAssembly(named.OriginalDefinition);

    /// <summary>
    /// Why an object-kind root cannot get a <c>TypeSpec</c>, or <c>null</c>
    /// when it can (<see cref="HasObjectContract"/>). A BCL generic
    /// (<c>KeyValuePair</c>, <c>ValueTuple</c>, <c>SortedDictionary</c>, …) is
    /// not transpiled at all.
    /// </summary>
    public string? ObjectRootRejectionReasonOf(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
            return "interfaces other than the recognized collection, dictionary and set "
                + "interfaces need polymorphic JSON, which the TypeScript context does not "
                + "support yet";

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return "it is not a class or struct, so it has no JSON object contract";

        if (HasObjectContract(type))
            return null;

        return "it is not a transpiled class or struct of this project or of a referenced "
            + "Metano package, so it has no TypeScript contract";
    }

    /// <summary>
    /// A class or struct that can carry its own JSON object contract;
    /// <c>object</c> has no properties to describe.
    /// </summary>
    private static bool IsUserClassOrStruct(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && type.SpecialType != SpecialType.System_Object;

    /// <summary>
    /// The FR-010 rule, evaluated against the type's declaring assembly: the
    /// emitter's rule (<c>[Transpile]</c>, or the
    /// <c>[assembly: TranspileAssembly]</c> opt-in) for a type of this
    /// project, and the package origin the frontend resolved for a type of a
    /// referenced library.
    /// </summary>
    private bool IsTranspiledInDeclaringAssembly(INamedTypeSymbol definition) =>
        IsInCurrentAssembly(definition)
            ? SymbolHelper.IsTranspilable(
                definition,
                _context.AssemblyWideTranspile,
                _context.CurrentAssembly
            )
            : IsPackageContract(definition);

    private bool IsInCurrentAssembly(INamedTypeSymbol type) =>
        _context.CurrentAssembly is null
        || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _context.CurrentAssembly);

    /// <summary>
    /// A type the frontend resolved to a referenced <c>[EmitPackage]</c>
    /// assembly (through its outermost container, the symbol the package
    /// exports).
    /// </summary>
    private bool IsPackageContract(INamedTypeSymbol type)
    {
        if (_context.TypeMapping is not { } typeMapping)
            return false;

        var outermost = type;

        while (outermost.ContainingType is { } container)
            outermost = container;

        return !SymbolHelper.HasIgnore(type)
            && !SymbolHelper.HasExternal(type)
            && typeMapping.CrossAssemblyOrigins.ContainsKey(
                outermost.OriginalDefinition.GetCrossAssemblyOriginKey()
            );
    }
}
