using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>[Name]</c> that renames a method where it is declared and called
/// (spec 013 FR-005, OQ-1 (a)). An overload of a dispatched group the
/// toolchain emits has none: its <c>[Name]</c> pins its fast-path suffix
/// (<see cref="OverloadSuffixPin"/>) and its dispatcher keeps the C# name.
/// Every other method — a single one, a conversion operator, an overload of a
/// type that emits no dispatcher (<c>[External]</c>, <c>[JsCallable]</c>,
/// <c>[PlainObject]</c>) — keeps
/// <c>[Name]</c> as its whole name. The one rule declarations, call sites and
/// claims share.
/// </summary>
public static class OverloadEmittedName
{
    /// <summary>
    /// <paramref name="method"/>'s <c>[Name]</c> for
    /// <paramref name="target"/>, or <c>null</c> when it has none or when the
    /// <c>[Name]</c> pins a suffix instead.
    /// </summary>
    public static string? NameOverrideOf(IMethodSymbol method, TargetLanguage? target)
    {
        if (method.GetNameOverride(target) is not { } name)
            return null;

        return PinsItsSuffix(method.OriginalDefinition, target) ? null : name;
    }

    private static bool PinsItsSuffix(IMethodSymbol method, TargetLanguage? target) =>
        OverloadSuffixPin.Applies(method)
        && method.ContainingType is { } type
        && PinsOverloadSuffixes(type, target)
        && IsDispatchedOverload(method, type, target);

    private static bool IsDispatchedOverload(
        IMethodSymbol method,
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        type.TypeKind == TypeKind.Interface
            ? InterfaceOverloadGroup.IsMember(method, target)
                && InterfaceOverloadGroup.IsDispatched(method, target)
            : MethodOverloadGroups.GroupOf(method, target) is { IsDispatched: true };

    /// <summary>
    /// Whether a <c>[Name]</c> on an overload of a dispatched group of
    /// <paramref name="type"/> pins a suffix (its group is emitted behind a
    /// dispatcher, or as fast paths): transpiled, and not <c>[External]</c>
    /// (it emits nothing, so several C# overloads can bind one JavaScript
    /// function by <c>[Name]</c>), <c>[JsCallable]</c> or <c>[PlainObject]</c>
    /// (no dispatcher either). A target that keeps only the primary of such a
    /// group declares it under the name call sites spell from this rule.
    /// </summary>
    public static bool PinsOverloadSuffixes(INamedTypeSymbol type, TargetLanguage? target) =>
        IrTypeRefMapper.IsTranspilableType(type, target)
        && !type.HasExternal()
        && !type.HasJsCallable()
        && !type.HasPlainObject();
}
