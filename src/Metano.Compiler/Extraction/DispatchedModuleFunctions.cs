using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The dispatched overloads of a type's exported module functions (spec 013
/// FR-032), the facts a target's barrel claim pass needs: one public record
/// per dispatched member carrying the member symbol, its overload identity
/// (<see cref="IrOverloadRef"/>) and the fast-path name (<see
/// cref="IrOverloadName"/>) its group gave it. The Roslyn grouping the facts
/// derive from stays internal to the core — a target consumes the extracted
/// IR, never the grouping types.
/// </summary>
public static class DispatchedModuleFunctions
{
    /// <summary>
    /// The dispatched overloads of <paramref name="type"/>'s exported module
    /// functions, in declaration order; empty when none of its members
    /// dispatches.
    /// </summary>
    public static IReadOnlyList<DispatchedModuleOverload> Of(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(SymbolHelper.IsExportedModuleFunction)
            .Select(member =>
                DispatchedMethodCalls.ClaimOf(member, target) is { } claim
                    ? new DispatchedModuleOverload(
                        member,
                        claim.Ref,
                        claim.Name,
                        claim.SignatureText
                    )
                    : null
            )
            .OfType<DispatchedModuleOverload>()
            .ToList();
}

/// <summary>
/// One dispatched member of a type's module functions: its symbol, its
/// overload identity and its fast-path name.
/// </summary>
public sealed record DispatchedModuleOverload(
    IMethodSymbol Member,
    IrOverloadRef Ref,
    IrOverloadName Name,
    string SignatureText
);
