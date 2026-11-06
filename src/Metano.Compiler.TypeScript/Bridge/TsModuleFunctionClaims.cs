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
/// reads the extracted IR — the shape-only <see cref="IrModuleFunction"/>s
/// the core's module-function extractor produces, each carrying its <see
/// cref="IrModuleFunction.OverloadRef"/> and <see
/// cref="IrModuleFunction.OverloadName"/> — not the core's Roslyn grouping,
/// and claims, in one fixed order, the dispatcher names (the C# member
/// names) and the module variables first, then each dispatched group's fast
/// paths group by group in declaration order, appending <c>$</c> until free
/// and reporting <c>MS0067</c> for every rename. Call sites, the module
/// emission, both registries and every consumer of <see
/// cref="ClaimedNamesOf"/> resolve names through the same instance, so a
/// call in any file spells the fast path its module declared.
/// </summary>
public sealed class TsModuleFunctionClaims
{
    private static readonly IReadOnlyDictionary<IMethodSymbol, ClaimedNames> NoClaims =
        new Dictionary<IMethodSymbol, ClaimedNames>(SymbolEqualityComparer.Default);

    private readonly Compilation _compilation;
    private readonly Dictionary<
        (INamedTypeSymbol Type, string MemberName, int Ordinal),
        ClaimedNames
    > _claimed;
    private readonly IReadOnlyDictionary<
        INamedTypeSymbol,
        IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
    > _claimsByType;
    private readonly IReadOnlyList<MetanoDiagnostic> _findings;

    private TsModuleFunctionClaims(
        Compilation compilation,
        Dictionary<(INamedTypeSymbol Type, string MemberName, int Ordinal), ClaimedNames> claimed,
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
        > claimsByType,
        IReadOnlyList<MetanoDiagnostic> findings
    )
    {
        _compilation = compilation;
        _claimed = claimed;
        _claimsByType = claimsByType;
        _findings = findings;
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
    /// the module-function types in barrel order. Each type's module
    /// functions are extracted shape-only (<see
    /// cref="IrModuleFunctionExtractor"/> with no compilation, so no bodies
    /// and no diagnostics) and the claims are read off the extracted IR.
    /// <paramref name="shapeOriginResolver"/> maps the extracted parameter
    /// types without staging cross-package dependencies — reading a type's
    /// shape for its claims imports nothing.
    /// </summary>
    public static TsModuleFunctionClaims Compute(
        Compilation compilation,
        IAssemblySymbol? assembly,
        IrTypeOriginResolver? shapeOriginResolver
    )
    {
        var extraction = new IrExtractionContext(shapeOriginResolver)
        {
            Target = TargetLanguage.TypeScript,
        };

        var shapes = assembly is { } assemblySymbol
            ? TypeScriptTransformContext
                .EnumerateTopLevelStaticTypes(assemblySymbol.GlobalNamespace)
                .Where(IsModuleFunctionType)
                .Select(type => new ClaimPass.TypeFunctions(type, ExtractShapes(type, extraction)))
                .ToList()
            : [];

        var pass = new ClaimPass(shapes);
        pass.Run();

        return new(compilation, pass.Claimed, pass.ClaimsByType, pass.Findings);
    }

    /// <summary>
    /// The shape of a type's module functions: names, parameters and the
    /// overload claims, never bodies — the claims are a function of the
    /// declaration, and shape-only extraction reports nothing.
    /// </summary>
    private static IReadOnlyList<IrModuleFunction> ExtractShapes(
        INamedTypeSymbol type,
        IrExtractionContext extraction
    )
    {
        using var _ = ExtractionDiagnostics.Suppress();

        return IrModuleFunctionExtractor.Extract(type, extraction);
    }

    /// <summary>The barrel names the claim pass found taken and renamed.</summary>
    public IReadOnlyList<MetanoDiagnostic> Findings => _findings;

    /// <summary>
    /// The dispatcher and fast path the barrel claimed for a dispatched
    /// module-function overload; <c>null</c> when this compilation cannot
    /// see the declaring type or the overload is not a claimed one.
    /// </summary>
    public ClaimedNames? ClaimOf(IrOverloadRef overload)
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

    /// <summary>
    /// The one claiming walk, run once per transform: it fills the barrel
    /// table and the per-type claims before the immutable <see
    /// cref="TsModuleFunctionClaims"/> is constructed.
    /// </summary>
    private sealed class ClaimPass
    {
        private readonly IReadOnlyList<TypeFunctions> _shapes;
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
        private readonly Dictionary<
            (INamedTypeSymbol Type, string MemberName, int Ordinal),
            ClaimedNames
        > _claimed = [];
        private readonly Dictionary<
            INamedTypeSymbol,
            Dictionary<IMethodSymbol, ClaimedNames>
        > _claimsByType = new(SymbolEqualityComparer.Default);
        private readonly List<MetanoDiagnostic> _findings = [];

        public ClaimPass(IReadOnlyList<TypeFunctions> shapes) => _shapes = shapes;

        /// <summary>A type's extracted functions beside the members they came from.</summary>
        internal sealed class TypeFunctions
        {
            public TypeFunctions(INamedTypeSymbol type, IReadOnlyList<IrModuleFunction> functions)
            {
                // The one exported-module-function predicate keeps exactly
                // the members the extractor turned into functions, so member
                // i produced function i; anything else is a bug in the join.
                var members = type.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(member => member.IsEmittedModuleFunction(TargetLanguage.TypeScript))
                    .ToList();

                if (members.Count != functions.Count)
                {
                    throw new InvalidOperationException(
                        $"'{type.Name}' extracted {functions.Count} module functions for "
                            + $"{members.Count} emitted members."
                    );
                }

                Type = type;
                Functions = functions;
                Pairs = members
                    .Zip(functions, (member, function) => (Member: member, Function: function))
                    .ToList();
            }

            public INamedTypeSymbol Type { get; }

            public IReadOnlyList<IrModuleFunction> Functions { get; }

            /// <summary>
            /// The extracted functions beside the members they came from, in
            /// the extractor's walk order.
            /// </summary>
            public IReadOnlyList<(IMethodSymbol Member, IrModuleFunction Function)> Pairs { get; }
        }

        public Dictionary<
            (INamedTypeSymbol Type, string MemberName, int Ordinal),
            ClaimedNames
        > Claimed => _claimed;

        public IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
        > ClaimsByType
        {
            get
            {
                var claims = new Dictionary<
                    INamedTypeSymbol,
                    IReadOnlyDictionary<IMethodSymbol, ClaimedNames>
                >(SymbolEqualityComparer.Default);

                foreach (var entry in _claimsByType)
                {
                    claims[entry.Key] = entry.Value;
                }

                return claims;
            }
        }

        public IReadOnlyList<MetanoDiagnostic> Findings => _findings;

        /// <summary>
        /// Two passes so the order a name is taken in stays the one the
        /// barrel has always used: pass 1 reserves every dispatcher and
        /// module variable of every type, pass 2 claims each dispatched
        /// group's fast paths against them.
        /// </summary>
        public void Run()
        {
            foreach (var shape in _shapes)
            {
                _claimsByType[shape.Type.OriginalDefinition] = new(SymbolEqualityComparer.Default);
            }

            foreach (var shape in _shapes)
            {
                ClaimDispatchers(shape);
            }

            foreach (var shape in _shapes)
            {
                ClaimFastPaths(shape);
            }
        }

        /// <summary>
        /// Pass 1: every exported function's dispatcher name joins the barrel
        /// table, and every overload of a group records the same dispatcher. A
        /// cross-class dispatcher clash stays where it is handled today (the
        /// export registry reports MS0020); the claim table only reserves the
        /// name.
        /// </summary>
        private void ClaimDispatchers(TypeFunctions shape)
        {
            var ordinals = OrdinalsOf(shape);

            foreach (var member in shape.Type.GetMembers())
            {
                switch (member)
                {
                    case IMethodSymbol method
                        when method.IsEmittedModuleFunction(TargetLanguage.TypeScript):
                        ClaimDispatcher(shape.Type, method, ordinals);
                        break;

                    case IFieldSymbol { DeclaredAccessibility: Accessibility.Public } field:
                        ClaimVariable(TypeScriptNaming.ToCamelCase(field.Name));
                        break;

                    case IPropertySymbol
                    {
                        DeclaredAccessibility: Accessibility.Public,
                        IsIndexer: false,
                    } property:
                        ClaimVariable(TypeScriptNaming.ToCamelCase(property.Name));
                        break;
                }
            }
        }

        /// <summary>
        /// Pass 2: one fast path per dispatched group, group by group in
        /// declaration order — the order the barrel has always claimed in,
        /// so a clean name goes to the group declared first, and inside a
        /// group to its first overload — claimed against everything pass 1
        /// reserved.
        /// </summary>
        private void ClaimFastPaths(TypeFunctions shape)
        {
            var claimed = shape
                .Pairs.Where(pair => pair.Function.OverloadRef is not null)
                .GroupBy(pair => pair.Function.OverloadRef!.MemberName)
                .SelectMany(group => group.OrderBy(pair => pair.Function.OverloadRef!.Ordinal));

            foreach (var (member, function) in claimed)
            {
                ClaimFastPath(shape.Type, member, function);
            }
        }

        private void ClaimDispatcher(
            INamedTypeSymbol type,
            IMethodSymbol member,
            IReadOnlyDictionary<IMethodSymbol, int> ordinals
        )
        {
            var dispatcher = FunctionNameOf(member);
            var ordinal = ordinals.TryGetValue(member, out var value) ? value : 0;

            _taken.Add(dispatcher);
            _claimed[(type.OriginalDefinition, member.Name, ordinal)] = new ClaimedNames(
                dispatcher,
                dispatcher
            );
        }

        private void ClaimFastPath(
            INamedTypeSymbol type,
            IMethodSymbol member,
            IrModuleFunction function
        )
        {
            var overload = function.OverloadRef!;
            var key = (type.OriginalDefinition, overload.MemberName, overload.Ordinal);

            if (
                !_claimed.TryGetValue(key, out var claimed) || function.OverloadName is not { } name
            )
                return;

            var candidate = claimed.Dispatcher + name.Suffix;
            var claimedFastPath = ClaimName(candidate);

            _claimed[key] = new ClaimedNames(claimed.Dispatcher, claimedFastPath);
            _claimsByType[type.OriginalDefinition][member] = new ClaimedNames(
                claimed.Dispatcher,
                claimedFastPath
            );

            if (claimedFastPath == candidate)
                return;

            _findings.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.OverloadNameShadowed,
                    $"Fast path '{type.Name}.{candidate}' of '{member.Name}' is already taken in "
                        + $"the barrel; it is emitted as '{claimedFastPath}' (spec 013 FR-032).",
                    member.Locations.FirstOrDefault()
                )
            );
        }

        private string ClaimName(string candidate)
        {
            var name = candidate;

            while (!_taken.Add(name))
                name += "$";

            return name;
        }

        private void ClaimVariable(string name) => _taken.Add(name);

        /// <summary>
        /// The overload's declaration ordinal in its dispatched group, the
        /// same identity a call site's <see cref="IrOverloadRef"/> carries;
        /// the extracted IR records it, so a member the IR did not dispatch
        /// stands alone at ordinal 0.
        /// </summary>
        private static IReadOnlyDictionary<IMethodSymbol, int> OrdinalsOf(TypeFunctions shape)
        {
            var ordinals = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);

            foreach (var (member, function) in shape.Pairs)
            {
                if (function.OverloadRef is { } overload)
                {
                    ordinals[member] = overload.Ordinal;
                }
            }

            return ordinals;
        }

        private static string FunctionNameOf(IMethodSymbol method) =>
            SymbolHelper.GetNameOverride(method, TargetLanguage.TypeScript)
            ?? TypeScriptNaming.ToCamelCase(method.Name);
    }
}

/// <summary>
/// The barrel names one exported module function claimed: its dispatcher
/// and its fast path, equal when the function stands alone.
/// </summary>
public sealed record ClaimedNames(string Dispatcher, string FastPath);
