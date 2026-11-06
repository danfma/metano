using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Decides which method calls are lowered to the exact overload Roslyn
/// resolved instead of the runtime dispatcher (spec 013 FR-018, DECIDED-11,
/// DECIDED-24): an instance call keeps its receiver, a static call keeps the
/// type, an extension call keeps the imported helper, an operator call names
/// the operator's fast path, and a method group binds its receiver. The core
/// holds the target-neutral facts — the overload belongs to a dispatched
/// group (two or more declared overloads, an inherited dispatcher, or an
/// anchored one) whose IR is available, declared in this compilation or in a
/// project reference, which loads as source; a type read from metadata only
/// (a DLL or a package) keeps the dispatcher call, since its claimed names
/// are unknown and the dispatcher is correct, only slower. A <c>base.M</c>
/// call keeps its own gate (<see
/// cref="IrExpressionExtractor.TryExtractBaseOverloadCall"/>), which binds
/// metadata-only bases too, since their root names come from metadata
/// (DECIDED-28).
/// </summary>
internal static class DispatchedMethodCalls
{
    /// <summary>
    /// The overload a call to <paramref name="method"/> targets directly;
    /// <c>null</c> when the call keeps the dispatcher.
    /// </summary>
    public static IrOverloadRef? RefOf(IMethodSymbol? method, TargetLanguage? target)
    {
        if (method is null || target == TargetLanguage.Dart)
            return null;

        var definition = method.OriginalDefinition;
        var containingType = definition.ContainingType;

        if (containingType is not { SpecialType: SpecialType.None })
            return null;

        if (IsExcludedMember(definition) || !HasSourceIr(containingType))
            return null;

        if (!IsEmittedContainingType(containingType, target))
            return null;

        if (SymbolHelper.IsContainerless(containingType) && !IsModuleExportable(definition))
            return null;

        if (HasUnresolvedTypeArguments(definition))
            return null;

        return IsDispatchedGroupMethod(definition, target)
            ? MethodOverloadGroups.RefOf(definition, target)
            : null;
    }

    /// <summary>
    /// A generic call whose type arguments carry no type the backend can
    /// spell — an error type from unresolved code, or an anonymous one, which
    /// no emitted declaration names — keeps the dispatcher (spec 013 FR-018).
    /// </summary>
    private static bool HasUnresolvedTypeArguments(IMethodSymbol method) =>
        method.TypeArguments.Any(type =>
            type.TypeKind == TypeKind.Error
            || (type is INamedTypeSymbol named && named.IsAnonymousType)
        );

    /// <summary>
    /// Whether the static method of a containerless class is a module
    /// function the backend emits: a public ordinary static — everything
    /// else (<c>[Emit]</c>, <c>[Import]</c>, inlined, non-public) has no
    /// fast path to call.
    /// </summary>
    private static bool IsModuleExportable(IMethodSymbol method) =>
        method.IsStatic
        && method.MethodKind == MethodKind.Ordinary
        && method.DeclaredAccessibility == Accessibility.Public;

    /// <summary>
    /// Whether <paramref name="type"/> — the declaring type of a candidate
    /// direct call — emits the member the call site needs: a transpilable
    /// class, or a transpiled interface, none of the keep-today's-emission
    /// shapes (<c>[External]</c>, a branded companion, <c>[PlainObject]</c>,
    /// <c>[JsCallable]</c> — DECIDED-10), whose functions live in a
    /// companion namespace.
    /// </summary>
    private static bool IsEmittedContainingType(INamedTypeSymbol type, TargetLanguage? target)
    {
        if (type.TypeKind == TypeKind.Interface)
            return MethodOverloadGroups.IsTranspiledInterface(type, target);

        return type.TypeKind is TypeKind.Class or TypeKind.Struct
            && IrTypeRefMapper.IsTranspilableType(type, target)
            && !SymbolHelper.HasExternal(type)
            && !SymbolHelper.HasBranded(type)
            && !SymbolHelper.HasPlainObject(type)
            && !SymbolHelper.HasJsCallable(type);
    }

    /// <summary>
    /// Whether <paramref name="method"/> belongs to a dispatched group
    /// (DECIDED-15): two or more declared overloads, an inherited dispatcher,
    /// or one member anchored on a dispatched root. A C# 14 extension block's
    /// member is declared on the block's synthetic type, which the groups do
    /// not cover — its call keeps the dispatcher until its claims exist.
    /// </summary>
    private static bool IsDispatchedGroupMethod(IMethodSymbol method, TargetLanguage? target) =>
        !SymbolHelper.IsExtensionBlockMember(method)
        && MethodOverloadGroups.GroupOf(method, target) is { IsDispatched: true };

    /// <summary>
    /// A member no generated call site may address directly: an
    /// <c>[Emit]</c> template, an <c>[Import]</c> facade or an inlined one —
    /// each lowers at the call site by its own rule (DECIDED-11).
    /// </summary>
    private static bool IsExcludedMember(IMethodSymbol method) =>
        SymbolHelper.HasEmit(method)
        || SymbolHelper.GetImport(method) is not null
        || SymbolHelper.IsInlinedAtCallSite(method);

    /// <summary>
    /// A declaring type with syntax: declared in this compilation or in a
    /// project reference, which loads as source (DECIDED-24). A metadata-only
    /// type keeps the dispatcher.
    /// </summary>
    private static bool HasSourceIr(INamedTypeSymbol type) =>
        !type.OriginalDefinition.DeclaringSyntaxReferences.IsEmpty;
}
