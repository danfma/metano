using System.Collections.Concurrent;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The Dart claim results (<see cref="DartOverloadClaims"/>) of every type one
/// transform emits, computed once per declaring type from its shape IR and
/// cached, so a type's own emission, every type that inherits from or
/// implements it, and every call site in any file read the same names (spec
/// 013 DECIDED-18). Call sites resolve an overload identity
/// (<see cref="IrOverloadRef"/>) through it.
/// </summary>
public sealed class DartOverloadClaimsRegistry : IDartOverloadNames
{
    private readonly Compilation _compilation;

    // A shape: no bodies, so no compilation, and nothing reported twice.
    private readonly IrExtractionContext _shapeExtraction;

    private readonly ConcurrentDictionary<INamedTypeSymbol, Lazy<DartOverloadClaims>> _claims = new(
        SymbolEqualityComparer.Default
    );

    public DartOverloadClaimsRegistry(Compilation compilation, IrExtractionContext extraction)
    {
        _compilation = compilation;
        _shapeExtraction = extraction with { Compilation = null };
    }

    /// <summary>The claims of <paramref name="type"/>, computed on first use.</summary>
    public DartOverloadClaims ClaimsOf(INamedTypeSymbol type) =>
        _claims
            .GetOrAdd(
                type.OriginalDefinition,
                declaration => new Lazy<DartOverloadClaims>(() => Compute(declaration))
            )
            .Value;

    /// <summary>
    /// The named constructor of the constructor overload a construction
    /// selected; <c>null</c> when the transform cannot see its declaring type.
    /// </summary>
    public string? ConstructorNameOf(IrOverloadRef overload) =>
        DeclaringTypeOf(overload) is { } type
            ? ClaimsOf(type).ConstructorNameAt(overload.Ordinal)
            : null;

    /// <summary>
    /// The fast path of the method overload a call selected — a class
    /// method's, an interface member's or a module function's; <c>null</c>
    /// when the transform cannot see its declaring type.
    /// </summary>
    public string? FastPathOf(IrOverloadRef overload) =>
        DeclaringTypeOf(overload) is { } type
            ? ClaimsOf(type).FastPathAt(overload.MemberName, overload.Ordinal)
            : null;

    private INamedTypeSymbol? DeclaringTypeOf(IrOverloadRef overload) =>
        IrTypeKeys.Find(overload.DeclaringType, _compilation);

    private DartOverloadClaims Compute(INamedTypeSymbol type)
    {
        using var _ = ExtractionDiagnostics.Suppress();

        if (type.TypeKind == TypeKind.Interface)
            return DartFlatClaimPass.Run(IrInterfaceExtractor.Extract(type, _shapeExtraction));

        if (DartTypeShapes.LowersToTopLevelFunctions(type))
        {
            return DartFlatClaimPass.Run(
                type.Name,
                IrModuleFunctionExtractor.Extract(type, _shapeExtraction)
            );
        }

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return DartOverloadClaims.None;

        var ir = IrClassExtractor.Extract(type, _shapeExtraction);

        return DartClassClaimPass.Run(
            new DartClassClaimInput(
                ir,
                IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes),
                DartTypeShapes.NamesOverloadMembers(ir),
                InheritedMembersOf(type, ir),
                FastPathOf
            )
        );
    }

    /// <summary>
    /// The members the class inherits from its emitted base, of every
    /// visibility; nothing when the base is not an emitted class.
    /// </summary>
    private IReadOnlyDictionary<string, DartClaimedMember> InheritedMembersOf(
        INamedTypeSymbol type,
        IrClassDeclaration ir
    ) =>
        ir.BaseType is IrNamedTypeRef { Semantics.IsTranspilable: true }
        && type.BaseType is { } baseType
        && !SymbolHelper.HasExternal(baseType)
            ? ClaimsOf(baseType).Members
            : DartOverloadClaims.None.Members;
}

/// <summary>
/// The names a Dart call site resolves a selected overload to (spec 013
/// DECIDED-18): the named constructor of a constructor overload, the fast path
/// of a method overload; <c>null</c> when the declaring type is not visible,
/// which keeps the source spelling.
/// </summary>
public interface IDartOverloadNames
{
    string? ConstructorNameOf(IrOverloadRef overload);

    string? FastPathOf(IrOverloadRef overload);
}

/// <summary>The names of a rewrite that resolves no overload: every call site keeps its source spelling.</summary>
public sealed class NoDartOverloadNames : IDartOverloadNames
{
    private NoDartOverloadNames() { }

    public static NoDartOverloadNames Instance { get; } = new();

    public string? ConstructorNameOf(IrOverloadRef overload) => null;

    public string? FastPathOf(IrOverloadRef overload) => null;
}
