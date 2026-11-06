using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The barrel-scope claim pass of the module-function fast paths (spec 013
/// FR-032, contracts/naming.md § Claim pass): every containerless class and
/// extension container one transform emits contributes its exported
/// functions to one namespace — the barrel — so two modules exporting a
/// <c>twiceX</c> fast path would clash on the re-export (TS2300). The pass
/// reads the extracted IR (<see cref="DispatchedModuleFunctions"/>), not the
/// core's Roslyn grouping, and claims, in one fixed order, the dispatcher
/// names (the C# member names) and the module variables first, then each
/// dispatched group's fast paths in declaration order, appending <c>$</c>
/// until free and reporting <c>MS0067</c> for every rename. Call sites, the
/// module emission, both registries and every consumer of <see
/// cref="ClaimedNamesOf"/> resolve names through the same instance, so a
/// call in any file spells the fast path its module declared.
/// </summary>
public sealed class TsModuleFunctionClaims
{
    private static readonly IReadOnlyDictionary<IMethodSymbol, ClaimedNames> NoClaims =
        new Dictionary<IMethodSymbol, ClaimedNames>(SymbolEqualityComparer.Default);

    private readonly Compilation _compilation;
    private readonly IReadOnlyList<INamedTypeSymbol> _moduleTypes;
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private readonly Dictionary<
        (INamedTypeSymbol Type, string MemberName, int Ordinal),
        (string Dispatcher, string FastPath)
    > _claimed = [];
    private readonly IReadOnlyDictionary<
        INamedTypeSymbol,
        IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
    > _claimsByType;
    private readonly List<MetanoDiagnostic> _findings = [];

    private TsModuleFunctionClaims(Compilation compilation, IAssemblySymbol? assembly)
    {
        _compilation = compilation;
        _moduleTypes = assembly is { } assemblySymbol
            ? TypeScriptTransformContext
                .EnumerateTopLevelStaticTypes(assemblySymbol.GlobalNamespace)
                .Where(IsModuleFunctionType)
                .ToList()
            : [];

        _claimsByType = Claim();
    }

    /// <summary>
    /// Whether <paramref name="type"/> emits module functions: a
    /// containerless class, or a static class holding exported classic
    /// extension methods — either way its exported statics flatten to
    /// top-level functions the barrel re-exports.
    /// </summary>
    public static bool IsModuleFunctionType(INamedTypeSymbol type) =>
        SymbolHelper.IsContainerless(type) || HoldsExportedExtension(type);

    /// <summary>
    /// One pass per transform: <paramref name="compilation"/> resolves an
    /// overload's declaring type and <paramref name="assembly"/> enumerates
    /// the module-function types in barrel order.
    /// </summary>
    public static TsModuleFunctionClaims Compute(
        Compilation compilation,
        IAssemblySymbol? assembly
    ) => new(compilation, assembly);

    /// <summary>The barrel names the claim pass found taken and renamed.</summary>
    public IReadOnlyList<MetanoDiagnostic> Findings => _findings;

    /// <summary>
    /// The dispatcher and fast path the barrel claimed for a dispatched
    /// module-function overload; <c>null</c> when this compilation cannot
    /// see the declaring type or the overload is not a claimed one.
    /// </summary>
    public (string Dispatcher, string FastPath)? ClaimOf(IrOverloadRef overload)
    {
        if (IrTypeKeys.Find(overload.DeclaringType, _compilation) is not { } declaringType)
            return null;

        return _claimed.TryGetValue(
            (declaringType.OriginalDefinition, overload.MemberName, overload.Ordinal),
            out var claimed
        )
            ? claimed
            : null;
    }

    /// <summary>
    /// The barrel names each exported module function of <paramref
    /// name="type"/> claimed, per member: the dispatcher and, on a
    /// dispatched group, the fast path beside it (the two equal when the
    /// function stands alone). The export registry and the extension-helper
    /// registry read this table instead of re-deriving the overload
    /// identity.
    /// </summary>
    public IReadOnlyDictionary<IMethodSymbol, ClaimedNames> ClaimedNamesOf(INamedTypeSymbol type) =>
        _claimsByType.TryGetValue(type.OriginalDefinition, out var claims) ? claims : NoClaims;

    /// <summary>
    /// The claims of every module type, in two passes so the order a name is
    /// taken in stays the one the barrel has always used: pass 1 reserves
    /// every dispatcher and module variable of every type, pass 2 claims
    /// each dispatched group's fast paths against them, and the per-member
    /// table <see cref="ClaimedNamesOf"/> publishes records what each member
    /// ended up with.
    /// </summary>
    private IReadOnlyDictionary<
        INamedTypeSymbol,
        IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
    > Claim()
    {
        var dispatchedByType = _moduleTypes
            .Select(type =>
                (
                    Type: type,
                    Overloads: DispatchedModuleFunctions.Of(type, TargetLanguage.TypeScript)
                )
            )
            .ToList();

        foreach (var (type, overloads) in dispatchedByType)
        {
            ClaimDispatchers(type, OrdinalsOf(overloads));
        }

        var claimsByType = new Dictionary<
            INamedTypeSymbol,
            IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
        >(SymbolEqualityComparer.Default);

        foreach (var (type, overloads) in dispatchedByType)
        {
            claimsByType[type.OriginalDefinition] = ClaimFastPaths(type, overloads);
        }

        return claimsByType;
    }

    /// <summary>
    /// Pass 1: every exported function's dispatcher name joins the barrel
    /// table, and every overload of a group records the same dispatcher. A
    /// cross-class dispatcher clash stays where it is handled today (the
    /// export registry reports MS0020); the claim table only reserves the
    /// name.
    /// </summary>
    private void ClaimDispatchers(
        INamedTypeSymbol type,
        IReadOnlyDictionary<IMethodSymbol, int> ordinals
    )
    {
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol method when SymbolHelper.IsExportedModuleFunction(method):
                    ClaimDispatcher(
                        type,
                        method,
                        ordinals.TryGetValue(method, out var ordinal) ? ordinal : 0
                    );
                    break;

                case IFieldSymbol { DeclaredAccessibility: Accessibility.Public } field:
                    ClaimVariable(type, field.Name);
                    break;

                case IPropertySymbol
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsIndexer: false,
                } property:
                    ClaimVariable(type, property.Name);
                    break;
            }
        }
    }

    /// <summary>
    /// Pass 2: one fast path per dispatched group, in declaration order,
    /// claimed against everything pass 1 reserved. Returns, per member, the
    /// names it ended up with — the table the export registry and the
    /// extension-helper registry read.
    /// </summary>
    private IReadOnlyDictionary<IMethodSymbol, ClaimedNames> ClaimFastPaths(
        INamedTypeSymbol type,
        IReadOnlyList<DispatchedModuleOverload> overloads
    )
    {
        var claims = new Dictionary<IMethodSymbol, ClaimedNames>(SymbolEqualityComparer.Default);

        foreach (var overload in overloads)
        {
            var key = (type, overload.Member.Name, overload.Ref.Ordinal);

            if (!_claimed.TryGetValue(key, out var claimed))
                continue;

            var candidate = claimed.Dispatcher + overload.Name.Suffix;
            var claimedFastPath = ClaimName(candidate);

            _claimed[key] = (claimed.Dispatcher, claimedFastPath);
            claims[overload.Member] = new ClaimedNames(claimed.Dispatcher, claimedFastPath);

            if (claimedFastPath == candidate)
                continue;

            Report(
                DiagnosticCodes.OverloadNameShadowed,
                MetanoDiagnosticSeverity.Warning,
                $"Fast path '{type.Name}.{candidate}' of '{overload.Member.Name}' is already taken in "
                    + $"the barrel; it is emitted as '{claimedFastPath}' (spec 013 FR-032).",
                overload.Member.Locations.FirstOrDefault()
            );
        }

        return claims;
    }

    private void ClaimDispatcher(INamedTypeSymbol type, IMethodSymbol method, int ordinal)
    {
        var dispatcher = FunctionNameOf(method);

        _taken.Add(dispatcher);
        _claimed[(type, method.Name, ordinal)] = (dispatcher, dispatcher);
    }

    private void ClaimVariable(INamedTypeSymbol type, string name)
    {
        _taken.Add(TypeScriptNaming.ToCamelCase(name));
    }

    private string ClaimName(string candidate)
    {
        var name = candidate;

        while (!_taken.Add(name))
            name += "$";

        return name;
    }

    /// <summary>
    /// Whether the static class exports classic extension methods — its
    /// exported statics flatten to top-level functions the barrel
    /// re-exports, so its dispatched extension groups claim fast paths even
    /// though the class itself keeps its namespace. A declaration view:
    /// reduced extension symbols never appear in a member list.
    /// </summary>
    private static bool HoldsExportedExtension(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(member =>
                member.IsExtensionMethod && SymbolHelper.IsExportedModuleFunction(member)
            );

    private static string FunctionNameOf(IMethodSymbol method) =>
        SymbolHelper.GetNameOverride(method, TargetLanguage.TypeScript)
        ?? TypeScriptNaming.ToCamelCase(method.Name);

    /// <summary>
    /// The overload's declaration ordinal in its dispatched group, the same
    /// identity a call site's <see cref="IrOverloadRef"/> carries; the
    /// extracted IR records it, so a member the IR did not dispatch stands
    /// alone at ordinal 0.
    /// </summary>
    private static IReadOnlyDictionary<IMethodSymbol, int> OrdinalsOf(
        IReadOnlyList<DispatchedModuleOverload> overloads
    )
    {
        var ordinals = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);

        foreach (var overload in overloads)
        {
            ordinals[overload.Member] = overload.Ref.Ordinal;
        }

        return ordinals;
    }

    private void Report(
        string code,
        MetanoDiagnosticSeverity severity,
        string message,
        Location? location
    ) => _findings.Add(new MetanoDiagnostic(severity, code, message, location));
}

/// <summary>
/// The barrel names one exported module function claimed: its dispatcher
/// and its fast path, equal when the function stands alone.
/// </summary>
public sealed record ClaimedNames(string Dispatcher, string FastPath);
