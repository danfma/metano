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
/// claims, in one fixed order, the dispatcher names (the C# member names)
/// and the module variables first, then each dispatched group's fast paths
/// in declaration order, appending <c>$</c> until free and reporting
/// <c>MS0067</c> for every rename. Call sites, the module emission and the
/// export registry all resolve names through the same instance, so a call in
/// any file spells the fast path its module declared.
/// </summary>
public sealed class TsModuleFunctionClaims
{
    private readonly Compilation _compilation;
    private readonly IReadOnlyList<INamedTypeSymbol> _moduleTypes;
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private readonly Dictionary<
        (INamedTypeSymbol Type, string MemberName, int Ordinal),
        (string Dispatcher, string FastPath)
    > _claimed = [];
    private readonly List<MetanoDiagnostic> _findings = [];

    private TsModuleFunctionClaims(Compilation compilation, IAssemblySymbol? assembly)
    {
        _compilation = compilation;
        _moduleTypes = assembly is { } assemblySymbol
            ? TypeScriptTransformContext
                .EnumerateTopLevelStaticTypes(assemblySymbol.GlobalNamespace)
                .Where(type => SymbolHelper.IsContainerless(type) || HoldsExportedExtension(type))
                .ToList()
            : [];
    }

    /// <summary>
    /// Whether <paramref name="type"/> emits module functions: a
    /// containerless class, or a static class holding exported classic
    /// extension methods — either way its exported statics flatten to
    /// top-level functions the barrel re-exports.
    /// </summary>
    public static bool IsModuleFunctionType(INamedTypeSymbol type) =>
        SymbolHelper.IsContainerless(type)
        || type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(member =>
                member.IsExtensionMethod && member.DeclaredAccessibility == Accessibility.Public
            );

    /// <summary>
    /// One pass per transform: <paramref name="compilation"/> resolves an
    /// overload's declaring type and <paramref name="assembly"/> enumerates
    /// the module-function types in barrel order.
    /// </summary>
    public static TsModuleFunctionClaims Compute(Compilation compilation, IAssemblySymbol? assembly)
    {
        var claims = new TsModuleFunctionClaims(compilation, assembly);

        claims.ClaimDispatchers();
        claims.ClaimFastPaths();

        return claims;
    }

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
    /// Pass 1: every exported function's dispatcher name joins the barrel
    /// table, and every overload of a group records the same dispatcher. A
    /// cross-class dispatcher clash stays where it is handled today (the
    /// export registry reports MS0020); the claim table only reserves the
    /// name.
    /// </summary>
    private void ClaimDispatchers()
    {
        foreach (var type in _moduleTypes)
        {
            foreach (var member in type.GetMembers())
            {
                switch (member)
                {
                    case IMethodSymbol method
                        when IsExportableFunction(method) || IsExportableExtension(method):
                        ClaimDispatcher(type, method);
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
    }

    /// <summary>
    /// Pass 2: one fast path per dispatched group, in declaration order,
    /// claimed against everything pass 1 reserved.
    /// </summary>
    private void ClaimFastPaths()
    {
        foreach (var type in _moduleTypes)
        {
            foreach (var group in MethodOverloadGroups.Of(type, TargetLanguage.TypeScript))
            {
                if (!group.IsDispatched)
                    continue;

                var suffixes = MethodOverloadGroups.SuffixesOf(
                    type,
                    group,
                    TargetLanguage.TypeScript
                );

                foreach (var member in group.Members)
                {
                    if (
                        !IsExportableFunction(member.Symbol)
                        && !IsExportableExtension(member.Symbol)
                    )
                        continue;

                    ClaimFastPath(type, member.Symbol, suffixes[member.Symbol]);
                }
            }
        }
    }

    private void ClaimDispatcher(INamedTypeSymbol type, IMethodSymbol method)
    {
        var dispatcher = FunctionNameOf(method);

        _taken.Add(dispatcher);
        _claimed[(type, method.Name, OrdinalOf(method))] = (dispatcher, dispatcher);
    }

    private void ClaimVariable(INamedTypeSymbol type, string name)
    {
        _taken.Add(TypeScriptNaming.ToCamelCase(name));
    }

    private void ClaimFastPath(INamedTypeSymbol type, IMethodSymbol method, string suffix)
    {
        var key = (type, method.Name, OrdinalOf(method));

        if (!_claimed.TryGetValue(key, out var claimed))
            return;

        var candidate = claimed.Dispatcher + suffix;
        var claimedFastPath = ClaimName(candidate);

        _claimed[key] = (claimed.Dispatcher, claimedFastPath);

        if (claimedFastPath == candidate)
            return;

        Report(
            DiagnosticCodes.OverloadNameShadowed,
            MetanoDiagnosticSeverity.Warning,
            $"Fast path '{type.Name}.{candidate}' of '{method.Name}' is already taken in "
                + $"the barrel; it is emitted as '{claimedFastPath}' (spec 013 FR-032).",
            method.Locations.FirstOrDefault()
        );
    }

    private string ClaimName(string candidate)
    {
        var name = candidate;

        while (!_taken.Add(name))
            name += "$";

        return name;
    }

    private static bool HoldsExportedExtension(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(member => member.IsExtensionMethod && IsExportableExtension(member));

    private static bool IsExportableFunction(IMethodSymbol method) =>
        method.IsStatic
        && method.MethodKind == MethodKind.Ordinary
        && method.DeclaredAccessibility == Accessibility.Public
        && !method.IsExtensionMethod;

    private static bool IsExportableExtension(IMethodSymbol method) =>
        method.ReducedFrom is not null
        || (method.IsExtensionMethod && method.DeclaredAccessibility == Accessibility.Public);

    private static string FunctionNameOf(IMethodSymbol method) =>
        SymbolHelper.GetNameOverride(method, TargetLanguage.TypeScript)
        ?? TypeScriptNaming.ToCamelCase(method.Name);

    /// <summary>
    /// The overload's declaration ordinal in its dispatched group, the same
    /// identity a call site's <see cref="IrOverloadRef"/> carries.
    /// </summary>
    private static int OrdinalOf(IMethodSymbol method)
    {
        var group = MethodOverloadGroups.GroupOf(method, TargetLanguage.TypeScript);

        return group?.MemberOf(method)?.Ordinal ?? 0;
    }

    private void Report(
        string code,
        MetanoDiagnosticSeverity severity,
        string message,
        Location? location
    ) => _findings.Add(new MetanoDiagnostic(severity, code, message, location));
}
