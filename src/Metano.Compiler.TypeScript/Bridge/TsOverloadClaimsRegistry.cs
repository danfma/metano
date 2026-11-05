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
/// A class's claims are a function of its shape alone: its IR is extracted
/// without bodies (names, visibility, parameters), so the result is the same
/// whichever file asks first, and a base declared in another project, whose
/// syntax this compilation cannot bind, is read the same way.
/// </para>
/// </summary>
public sealed class TsOverloadClaimsRegistry(TypeScriptTransformContext context)
{
    private static readonly IReadOnlyDictionary<string, TsClaimedMember> NothingInherited =
        new Dictionary<string, TsClaimedMember>(StringComparer.Ordinal);

    private readonly TypeScriptTransformContext _context = context;
    private readonly IrToTsTypeOverrides? _typeOverrides = UnrecordedTypeOverridesOf(context);
    private readonly IrTypeOriginResolver? _shapeOriginResolver = UnrecordedOriginResolverOf(
        context
    );
    private readonly ConcurrentDictionary<INamedTypeSymbol, Lazy<TsOverloadClaims>> _claims = new(
        SymbolEqualityComparer.Default
    );

    /// <summary>
    /// Whether a class needs a claim pass: it declares an overload group, or
    /// it extends an emitted class whose static side it must claim against.
    /// </summary>
    internal static bool AppliesTo(IrClassDeclaration ir) =>
        HasOverloadGroup(ir) || IrToTsClassBridge.BuildExtends(ir) is TsNamedType;

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
                constructor =>
                    IrToTsConstructorFactoryBridge.SignatureOf(ir, constructor, _typeOverrides)
            )
        );
    }

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
        || (ir.Members ?? []).Any(member => member is IrMethodDeclaration { Overloads.Count: > 0 });
}
