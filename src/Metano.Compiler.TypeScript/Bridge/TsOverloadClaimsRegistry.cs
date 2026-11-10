using System.Collections.Concurrent;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The claim results (<see cref="TsOverloadClaims"/>) of every class one
/// transform emits, computed once per declaring type and cached, so a class's
/// own emission and every class that inherits from it read the same names
/// (spec 013 DECIDED-18). Shared by the parallel per-file transforms.
/// <para>
/// A class's claims are a function of its shape — its IR is extracted
/// without bodies (names, visibility, parameters), so the result is the same
/// whichever file asks first, and a base declared in another project, whose
/// syntax this compilation cannot bind, is read the same way — and of one
/// compilation-wide fact: which non-public factories code outside the class
/// body calls (FR-011, <see cref="ConstructorAccessFacts"/>). That fact is
/// known only for this compilation's types; a base from another project is
/// judged by its declared accessibility, so a factory its own package widened
/// reads narrower here, and FR-026 then qualifies a derived factory it could
/// have kept — a different name, never a clash.
/// </para>
/// <para>
/// Call sites reach the constructor claims by overload identity
/// (<see cref="ConstructorNamesOf(IrOverloadRef)"/>), which finds the
/// declaring type by its <see cref="IrTypeKey"/>.
/// </para>
/// </summary>
public sealed class TsOverloadClaimsRegistry(
    TypeScriptTransformContext context,
    ConstructorAccessFacts constructorAccessFacts
)
{
    private static readonly IReadOnlyDictionary<string, TsClaimedMember> NothingInherited =
        new Dictionary<string, TsClaimedMember>(StringComparer.Ordinal);

    private readonly TypeScriptTransformContext _context = context;
    private readonly ConstructorAccessFacts _constructorAccessFacts = constructorAccessFacts;
    private readonly IrToTsTypeOverrides? _typeOverrides = UnrecordedTypeOverridesOf(context);
    private readonly IrTypeOriginResolver? _shapeOriginResolver = UnrecordedOriginResolverOf(
        context
    );
    private readonly ConcurrentDictionary<INamedTypeSymbol, Lazy<TsOverloadClaims>> _claims = new(
        SymbolEqualityComparer.Default
    );
    private readonly ConcurrentDictionary<
        INamedTypeSymbol,
        Lazy<TsInterfaceFastPaths>
    > _interfaceClaims = new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<
        INamedTypeSymbol,
        Lazy<TsCompanionPlan?>
    > _companionPlans = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Whether a class needs a claim pass: it declares an overload group, or
    /// it extends an emitted class whose static side it must claim against.
    /// </summary>
    internal static bool AppliesTo(IrClassDeclaration ir) =>
        HasOverloadGroup(ir) || IrToTsClassBridge.BuildExtends(ir) is TsNamedType;

    /// <summary>
    /// The emitted fast-path name of a root overload (spec 013 FR-025): a
    /// class method's as its class claimed it, an interface member's as its
    /// interface declares it; <c>null</c> when this compilation cannot see
    /// the declaring type.
    /// </summary>
    internal string? RootFastPathOf(IrOverloadRef root)
    {
        if (IrTypeKeys.Find(root.DeclaringType, _context.Compilation) is not { } type)
            return null;

        // A containerless class or an extension container emits its
        // dispatched groups as module functions, whose fast paths the
        // barrel-scope claim pass names (spec 013 FR-032) — not as class
        // members.
        if (TsModuleFunctionClaims.IsModuleFunctionType(type))
            return _context.ModuleFunctionClaims.ClaimOf(root)?.FastPath;

        if (type.TypeKind == TypeKind.Interface)
            return InterfaceFastPathsOf(type).NameAt(root.MemberName, root.Ordinal);

        // A branded wrapper over a primitive emits its static groups as the
        // functions of its companion namespace, named by the companion's
        // plan (#569); any other wrapper is an ordinary class.
        return CompanionPlanOf(type) is { } companion
            ? companion.FastPathAt(root.MemberName, root.Ordinal)
            : For(type).FastPathNameAt(root.MemberName, root.Ordinal);
    }

    /// <summary>
    /// The companion plan of a branded struct, computed on first use from
    /// its shape; <c>null</c> for a type that emits no companion namespace.
    /// </summary>
    private TsCompanionPlan? CompanionPlanOf(INamedTypeSymbol type)
    {
        if (!TypeTransformer.IsBrandedStruct(type))
            return null;

        return _companionPlans
            .GetOrAdd(
                type.OriginalDefinition,
                declaration => new Lazy<TsCompanionPlan?>(() =>
                    TsCompanionPlan.Of(ShapeOf(declaration))
                )
            )
            .Value;
    }

    /// <summary>The fast-path names of an interface, computed on first use.</summary>
    internal TsInterfaceFastPaths InterfaceFastPathsOf(INamedTypeSymbol type) =>
        _interfaceClaims
            .GetOrAdd(
                type.OriginalDefinition,
                declaration => new Lazy<TsInterfaceFastPaths>(() =>
                    TsInterfaceFastPaths.Of(InterfaceShapeOf(declaration))
                )
            )
            .Value;

    /// <summary>
    /// The selector and factory names of the constructor overload a call site
    /// identifies (spec 013 DECIDED-18); <c>null</c> when this compilation
    /// cannot see the declaring type, which keeps the dispatcher call.
    /// </summary>
    internal TsConstructorOverloadNames? ConstructorNamesOf(IrOverloadRef overload) =>
        IrTypeKeys.Find(overload.DeclaringType, _context.Compilation) is { } type
            ? For(type).ConstructorNamesOf(overload.Ordinal)
            : null;

    /// <summary>The claims of <paramref name="type"/>, computed on first use.</summary>
    internal TsOverloadClaims For(INamedTypeSymbol type) =>
        _claims
            .GetOrAdd(
                type.OriginalDefinition,
                declaration => new Lazy<TsOverloadClaims>(() => Compute(declaration))
            )
            .Value;

    private TsOverloadClaims Compute(INamedTypeSymbol type)
    {
        var ir = ShapeOf(type);

        return TsOverloadClaimPass.Run(
            new TsClaimPassInput(
                ir,
                _context.ResolveTsName(type),
                InheritedStaticsOf(type, ir),
                InheritedInstancesOf(type, ir),
                constructor =>
                    IrToTsConstructorFactoryBridge.SignatureOf(
                        ir,
                        constructor,
                        IsWidened(type, constructor),
                        _typeOverrides
                    ),
                RootFastPathOf
            )
        );
    }

    /// <summary>Whether code outside the class body calls the overload's factory (FR-011).</summary>
    private bool IsWidened(INamedTypeSymbol type, IrConstructorDeclaration constructor) =>
        _constructorAccessFacts.OutsideCallOfFactory(type, constructor.DeclarationOrdinal)
            is not null;

    private IrClassDeclaration ShapeOf(INamedTypeSymbol type)
    {
        using var _ = ExtractionDiagnostics.Suppress();

        var extraction = _context.Extraction with
        {
            Compilation = null,
            OriginResolver = _shapeOriginResolver,
        };

        return IrEmissionNormalizer.Normalize(IrClassExtractor.Extract(type, extraction), type);
    }

    private IrInterfaceDeclaration InterfaceShapeOf(INamedTypeSymbol type)
    {
        using var _ = ExtractionDiagnostics.Suppress();

        return IrInterfaceExtractor.Extract(type, _context.Extraction with { Compilation = null });
    }

    /// <summary>
    /// The instance side the class inherits from its emitted base, of every
    /// visibility (claim step 7); nothing when the base is not an emitted
    /// class.
    /// </summary>
    private IReadOnlyDictionary<string, TsClaimedMember> InheritedInstancesOf(
        INamedTypeSymbol type,
        IrClassDeclaration ir
    ) =>
        IrToTsClassBridge.BuildExtends(ir) is TsNamedType && EmittedBaseOf(type, ir) is { } baseType
            ? For(baseType).Instances
            : NothingInherited;

    /// <summary>
    /// The static side the class inherits from its emitted base, rewritten
    /// into its own type parameters; nothing when the base is not an emitted
    /// class.
    /// </summary>
    private IReadOnlyDictionary<string, TsClaimedMember> InheritedStaticsOf(
        INamedTypeSymbol type,
        IrClassDeclaration ir
    )
    {
        if (IrToTsClassBridge.BuildExtends(ir) is not TsNamedType extendsType)
            return NothingInherited;

        if (EmittedBaseOf(type, ir) is not { } baseType)
            return NothingInherited;

        var baseTypeParameterNames = baseType
            .OriginalDefinition.TypeParameters.Select(parameter => parameter.Name)
            .ToList();

        return TsFactoryInheritance.Inherit(
            For(baseType).Statics,
            baseTypeParameterNames,
            extendsType,
            (ir.TypeParameters ?? []).Select(parameter => parameter.Name).ToList()
        );
    }

    /// <summary>
    /// The base class of a class that extends one, when the target emits it:
    /// not an erased or BCL base, and not an ambient (<c>[External]</c>) one.
    /// </summary>
    private static INamedTypeSymbol? EmittedBaseOf(INamedTypeSymbol type, IrClassDeclaration ir) =>
        ir.BaseType is IrNamedTypeRef { Semantics.IsTranspilable: true }
        && type.BaseType is { } baseType
        && !SymbolHelper.HasExternal(baseType)
            ? baseType
            : null;

    /// <summary>
    /// The type mapping of the class emitter's factory parameters, without
    /// recording the packages it maps to: reading a type's shape for its
    /// claims imports nothing.
    /// </summary>
    private static IrToTsTypeOverrides? UnrecordedTypeOverridesOf(
        TypeScriptTransformContext context
    ) =>
        context.TypeMapping is { } typeMapping
            ? new BclExportTypeOverrides(
                typeMapping.BclExportMap,
                new CrossPackageDependencyLedger()
            )
            : null;

    private static IrTypeOriginResolver? UnrecordedOriginResolverOf(
        TypeScriptTransformContext context
    ) =>
        context.TypeMapping is { } typeMapping
            ? IrTypeOriginResolverFactory.CreateUnrecorded(typeMapping)
            : null;

    private static bool HasOverloadGroup(IrClassDeclaration ir) =>
        ir.Constructor is { Overloads.Count: > 0 }
        || (ir.Members ?? []).Any(member =>
            member is IrMethodDeclaration { OverloadName: not null }
        );
}
