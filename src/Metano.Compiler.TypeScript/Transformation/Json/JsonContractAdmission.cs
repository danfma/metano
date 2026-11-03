using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Decides which object types the generated context can describe with a
/// <c>TypeSpec</c>: the ones the closure discovers on its own, and the ones a
/// <c>[JsonSerializable]</c> root may name explicitly.
/// </summary>
internal sealed class JsonContractAdmission(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    /// <summary>
    /// The closure rule: a non-generic transpilable class or struct of the
    /// current assembly (nested ones included). Interfaces (which need
    /// polymorphic JSON), generic types and types from referenced packages
    /// are not discovered.
    /// </summary>
    public bool IsDiscoverable(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && IsNonGenericClassOrStruct(named)
        && IsInCurrentAssembly(named)
        && SymbolHelper.IsTranspilable(
            named,
            _context.AssemblyWideTranspile,
            _context.CurrentAssembly
        );

    /// <summary>
    /// Why an object-kind root cannot get a <c>TypeSpec</c>, or <c>null</c>
    /// when it can. A root may also name a transpiled type of a referenced
    /// Metano package, which the context imports. A closed generic is reported
    /// as not supported yet only when its definition is transpiled; a BCL
    /// generic (<c>KeyValuePair</c>, <c>ValueTuple</c>, <c>SortedDictionary</c>,
    /// …) is not transpiled at all.
    /// </summary>
    public string? ObjectRootRejectionReasonOf(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
            return "interfaces other than the recognized collection, dictionary and set "
                + "interfaces need polymorphic JSON, which the TypeScript context does not "
                + "support yet";

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return "it is not a class or struct, so it has no JSON object contract";

        if (type.IsGenericType && IsTranspiledContract(type.OriginalDefinition))
            return "closed generic object types are not supported by the TypeScript context yet";

        if (!type.IsGenericType && (IsDiscoverable(type) || IsPackageContract(type)))
            return null;

        return "it is not a transpiled class or struct of this project or of a referenced "
            + "Metano package, so it has no TypeScript contract";
    }

    private bool IsTranspiledContract(INamedTypeSymbol definition) =>
        IsPackageContract(definition)
        || (
            IsInCurrentAssembly(definition)
            && SymbolHelper.IsTranspilable(
                definition,
                _context.AssemblyWideTranspile,
                _context.CurrentAssembly
            )
        );

    private static bool IsNonGenericClassOrStruct(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Class or TypeKind.Struct, IsGenericType: false }
        && type.SpecialType != SpecialType.System_Object;

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
