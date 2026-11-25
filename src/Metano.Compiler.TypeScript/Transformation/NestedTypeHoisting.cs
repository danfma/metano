using System.Collections.Concurrent;
using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Decides which nested types the TypeScript target declares at module level (spec
/// 011 FR-040). A TypeScript <c>namespace</c> merged with its owner compiles to an
/// IIFE every bundler keeps, so a container emitted as a class hoists its nested
/// types instead: each is declared module-level under its module-local identifier
/// (<c>Owner$Inner</c>, <see cref="ModuleLocalNaming"/>), the owner exposes it through
/// a static getter and an erased <c>declare namespace</c> keeps the type
/// <c>Owner.Inner</c>. A <c>private</c> nested type is module-level whatever its
/// container (#412).
/// <para>
/// A container hoists only when every non-private nested type it emits is a
/// declaration the hoisting can name and expose: a class (which hoists its own nested
/// types the same way, or has none), an enum, an interface or a delegate without
/// nested types of its own, and none that emits module functions beside its
/// declaration (a guard, the helpers of a <c>[PlainObject]</c>, a module-shaped
/// static class, a branded companion, a JSX component). Any other container keeps the
/// companion namespace of its nested types.
/// </para>
/// </summary>
internal sealed class NestedTypeHoisting(
    bool assemblyWideTranspile,
    IAssemblySymbol? currentAssembly
)
{
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _hoists = new(
        SymbolEqualityComparer.Default
    );

    /// <summary>
    /// Whether the nested <paramref name="type"/> is emitted: whenever it is
    /// transpilable, whatever its C# accessibility, since its container's members (and,
    /// for <c>internal</c>, other files) reference it. Assembly-wide opt-in already
    /// rejects implicit nested types; the guard keeps the explicit-<c>[Transpile]</c>
    /// path equally strict.
    /// </summary>
    public bool Emits(INamedTypeSymbol type) =>
        IsEmitted(type, assemblyWideTranspile, currentAssembly);

    /// <summary>
    /// <see cref="Emits"/> for a caller that runs before the transform context exists.
    /// </summary>
    public static bool IsEmitted(
        INamedTypeSymbol type,
        bool assemblyWideTranspile,
        IAssemblySymbol? currentAssembly
    ) =>
        !type.IsImplicitlyDeclared
        && SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly);

    /// <summary>
    /// True when <paramref name="container"/> declares its non-private nested types
    /// at module level.
    /// </summary>
    public bool HoistsNestedTypesOf(INamedTypeSymbol container) =>
        _hoists.GetOrAdd(container.OriginalDefinition, Decide);

    /// <summary>
    /// True when <paramref name="type"/> is a nested type declared at module level:
    /// a <c>private</c> one, or one its container hoists.
    /// </summary>
    public bool IsModuleLevel(INamedTypeSymbol type) =>
        type.ContainingType is { } container
        && Emits(type)
        && (SymbolHelper.IsContainerPrivateNestedType(type) || HoistsNestedTypesOf(container));

    /// <summary>
    /// True when other modules reach the module-level <paramref name="type"/> through
    /// its owner (<c>Owner.Inner</c>): no type on its containment path is
    /// <c>private</c>.
    /// </summary>
    public bool IsExposed(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (SymbolHelper.IsContainerPrivateNestedType(current))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The module-local identifier <paramref name="type"/> is declared under —
    /// its containment path joined by <see cref="ModuleLocalNaming.Separator"/> — or
    /// <c>null</c> when it is not declared at module level.
    /// </summary>
    public string? ModuleLevelName(INamedTypeSymbol type)
    {
        if (!IsModuleLevel(type))
            return null;

        var qualifiedName = IrTypeRefMapper.BuildQualifiedName(type, TargetLanguage.TypeScript);

        return qualifiedName.Replace(".", ModuleLocalNaming.Separator);
    }

    /// <summary>
    /// The dotted qualified names of the module-level nested types under
    /// <paramref name="type"/>, at every depth.
    /// </summary>
    public IEnumerable<string> ModuleLevelNamesUnder(INamedTypeSymbol type) =>
        EmittedNestedTypes(type)
            .SelectMany(nested =>
                ModuleLevelNamesUnder(nested)
                    .Concat(
                        IsModuleLevel(nested)
                            ?
                            [
                                IrTypeRefMapper.BuildQualifiedName(
                                    nested,
                                    TargetLanguage.TypeScript
                                ),
                            ]
                            : []
                    )
            );

    private bool Decide(INamedTypeSymbol container) =>
        EmitsAsClass(container)
        && EmittedNestedTypes(container)
            .Where(nested => !SymbolHelper.IsContainerPrivateNestedType(nested))
            .All(IsHoistable);

    private bool IsHoistable(INamedTypeSymbol nested)
    {
        if (EmitsModuleFunctions(nested))
            return false;

        if (EmitsAsClass(nested))
            return !HasExposedNestedTypes(nested) || HoistsNestedTypesOf(nested);

        return nested.TypeKind is TypeKind.Enum or TypeKind.Interface or TypeKind.Delegate
            && !HasExposedNestedTypes(nested);
    }

    private bool HasExposedNestedTypes(INamedTypeSymbol type) =>
        EmittedNestedTypes(type).Any(nested => !SymbolHelper.IsContainerPrivateNestedType(nested));

    private IEnumerable<INamedTypeSymbol> EmittedNestedTypes(INamedTypeSymbol type) =>
        type.GetTypeMembers().Where(Emits);

    /// <summary>
    /// Whether <paramref name="type"/> lowers to a TypeScript class declaration that
    /// can hold the static getters of its nested types.
    /// </summary>
    private static bool EmitsAsClass(INamedTypeSymbol type) =>
        (type.IsRecord || type.TypeKind is TypeKind.Class or TypeKind.Struct)
        && !SymbolHelper.HasImport(type)
        && !SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript)
        && !SymbolHelper.HasJsCallable(type)
        && !SymbolHelper.HasPlainObject(type)
        && !SymbolHelper.HasJsTuple(type)
        && !SymbolHelper.IsJsxComponent(type)
        && !TsCompanionPlan.AppliesTo(type)
        && !TypeTransformer.IsModuleShapedStaticClass(type)
        && !SymbolHelper.IsJsonSerializerContext(type)
        && !type.GetMembers().Any(member => member.HasModuleEntryPoint());

    /// <summary>
    /// Whether <paramref name="type"/> emits module functions beside its declaration,
    /// which a hoisted nested type has no owner member to expose through.
    /// </summary>
    private static bool EmitsModuleFunctions(INamedTypeSymbol type) =>
        SymbolHelper.HasGenerateGuard(type)
        || SymbolHelper.HasPlainObject(type)
        || TsCompanionPlan.AppliesTo(type)
        || SymbolHelper.IsJsxComponent(type)
        || TypeTransformer.IsModuleShapedStaticClass(type);
}
