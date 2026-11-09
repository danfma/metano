using System.Collections.Frozen;
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
/// reads the extracted IR — the shape-only <see cref="IrModuleFunction"/>
/// each emitted member becomes, carrying its <see
/// cref="IrModuleFunction.OverloadRef"/> and <see
/// cref="IrModuleFunction.OverloadName"/> — not the core's Roslyn grouping,
/// and claims, in one fixed order, the dispatcher names (the C# member
/// names), the module variables and the local names the <c>[Import]</c>
/// facades bind first, then each dispatched group's fast paths group by
/// group in declaration order, appending <c>$</c> until free and reporting
/// <c>MS0067</c> for every rename. Call sites, the module emission, both
/// registries and every consumer of <see cref="ClaimedNamesOf"/> resolve
/// names through the same instance, so a call in any file spells the fast
/// path its module declared.
/// </summary>
public sealed class TsModuleFunctionClaims
{
    private readonly Compilation _compilation;
    private readonly FrozenDictionary<OverloadKey, ClaimedNames> _byOverload;
    private readonly FrozenDictionary<
        INamedTypeSymbol,
        FrozenDictionary<IMethodSymbol, ClaimedNames>
    > _byType;

    private TsModuleFunctionClaims(
        Compilation compilation,
        FrozenDictionary<OverloadKey, ClaimedNames> byOverload,
        FrozenDictionary<INamedTypeSymbol, FrozenDictionary<IMethodSymbol, ClaimedNames>> byType,
        IReadOnlyList<MetanoDiagnostic> findings
    )
    {
        _compilation = compilation;
        _byOverload = byOverload;
        _byType = byType;
        Findings = findings;
    }

    /// <summary>The barrel names the claim pass found taken and renamed.</summary>
    public IReadOnlyList<MetanoDiagnostic> Findings { get; }

    /// <summary>
    /// The dispatcher and fast path the barrel claimed for a dispatched
    /// module-function overload; <c>null</c> when this compilation cannot
    /// see the declaring type or the overload is not a claimed one.
    /// </summary>
    public ClaimedNames? ClaimOf(IrOverloadRef overload)
    {
        if (IrTypeKeys.Find(overload.DeclaringType, _compilation) is not { } declaringType)
            return null;

        var key = new OverloadKey(
            declaringType.OriginalDefinition,
            overload.MemberName,
            overload.Ordinal
        );

        return _byOverload.TryGetValue(key, out var claimed) ? claimed : null;
    }

    /// <summary>
    /// The barrel names each exported module function of <paramref
    /// name="type"/> claimed, per member: the dispatcher and the fast path
    /// beside it — the two equal when the function stands alone. The export
    /// registry and the extension-helper registry read this table instead
    /// of re-deriving the overload identity; it holds exactly the members
    /// <see cref="ClaimOf"/> answers for, plus the standalone ones.
    /// </summary>
    public IReadOnlyDictionary<IMethodSymbol, ClaimedNames> ClaimedNamesOf(INamedTypeSymbol type) =>
        _byType.TryGetValue(type.OriginalDefinition, out var claims)
            ? claims
            : FrozenDictionary<IMethodSymbol, ClaimedNames>.Empty;

    /// <summary>
    /// One pass per transform: <paramref name="compilation"/> resolves an
    /// overload's declaring type and <paramref name="assembly"/> enumerates
    /// the module-function types in barrel order. Each emitted member is
    /// extracted shape-only (<see cref="IrModuleFunctionExtractor"/> with no
    /// compilation, so no bodies and no diagnostics) and the claims are read
    /// off the extracted IR. <paramref name="shapeOriginResolver"/> maps the
    /// extracted parameter types without staging cross-package dependencies
    /// — reading a type's shape for its claims imports nothing.
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

        var staticTypes = assembly is { } assemblySymbol
            ? TypeScriptTransformContext
                .EnumerateTopLevelStaticTypes(assemblySymbol.GlobalNamespace)
                .ToList()
            : [];

        var shapes = staticTypes
            .Where(IsModuleFunctionType)
            .Select(type => ClaimPass.TypeFunctions.Extract(type, extraction))
            .ToList();

        var pass = new ClaimPass(staticTypes, shapes);
        pass.Run();

        return pass.Seal(compilation);
    }

    /// <summary>
    /// Whether <paramref name="type"/> emits module functions: exactly the
    /// static classes the transformer flattens (<see
    /// cref="TypeTransformer.IsModuleShapedStaticClass"/>) — a containerless
    /// class or one holding any extension member, classic or in a C# 14
    /// block, whatever that member's accessibility or attributes. Which of
    /// its members claim names is decided per member (<see
    /// cref="SymbolHelper.IsEmittedModuleFunction"/>), never here, so every
    /// flattened group has a claim.
    /// </summary>
    public static bool IsModuleFunctionType(INamedTypeSymbol type) =>
        TypeTransformer.IsModuleShapedStaticClass(type);

    /// <summary>
    /// The identity a call site's <see cref="IrOverloadRef"/> names an
    /// overload by: its declaring type, member name and declaration ordinal.
    /// </summary>
    private readonly record struct OverloadKey(
        INamedTypeSymbol Type,
        string MemberName,
        int Ordinal
    );

    /// <summary>
    /// The one claiming walk, run once per transform: it fills one claim
    /// per emitted member, then <see cref="Seal"/> indexes them for the
    /// immutable <see cref="TsModuleFunctionClaims"/>.
    /// </summary>
    private sealed class ClaimPass
    {
        private readonly IReadOnlyList<INamedTypeSymbol> _staticTypes;
        private readonly IReadOnlyList<TypeFunctions> _shapes;
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
        private readonly Dictionary<IMethodSymbol, MemberClaim> _claims = new(
            SymbolEqualityComparer.Default
        );
        private readonly List<MetanoDiagnostic> _findings = [];

        public ClaimPass(
            IReadOnlyList<INamedTypeSymbol> staticTypes,
            IReadOnlyList<TypeFunctions> shapes
        )
        {
            _staticTypes = staticTypes;
            _shapes = shapes;
        }

        /// <summary>
        /// Two passes so the order a name is taken in stays the one the
        /// barrel has always used: pass 1 reserves every dispatcher and
        /// module variable of every module type and the <c>[Import]</c>
        /// binding of every static type, pass 2 claims each dispatched
        /// group's fast paths against them.
        /// </summary>
        public void Run()
        {
            foreach (var shape in _shapes)
            {
                ReserveNames(shape);
            }

            foreach (var type in _staticTypes)
            {
                ReserveImportBindings(type);
            }

            foreach (var shape in _shapes)
            {
                ClaimFastPaths(shape);
            }
        }

        /// <summary>
        /// The claims indexed both ways a consumer asks: by overload for a
        /// call site, by type and member for the registries. Both indexes
        /// come from the same claims, so they cover the same members.
        /// </summary>
        public TsModuleFunctionClaims Seal(Compilation compilation)
        {
            var byOverload = new Dictionary<OverloadKey, ClaimedNames>();

            var byType = new Dictionary<INamedTypeSymbol, Dictionary<IMethodSymbol, ClaimedNames>>(
                SymbolEqualityComparer.Default
            );

            foreach (var (member, claim) in _claims)
            {
                if (claim.Overload is { } overload)
                {
                    var key = new OverloadKey(claim.Type, overload.MemberName, overload.Ordinal);
                    byOverload[key] = claim.Names;
                }

                MembersOf(byType, claim.Type)[member] = claim.Names;
            }

            var frozenByType = byType
                .Select(entry =>
                    KeyValuePair.Create(
                        entry.Key,
                        entry.Value.ToFrozenDictionary(SymbolEqualityComparer.Default)
                    )
                )
                .ToFrozenDictionary(SymbolEqualityComparer.Default);

            return new TsModuleFunctionClaims(
                compilation,
                byOverload.ToFrozenDictionary(),
                frozenByType,
                _findings.ToList().AsReadOnly()
            );
        }

        /// <summary>
        /// Pass 1: every emitted function's dispatcher name joins the barrel
        /// table and records the member's claim — the dispatcher on both
        /// sides until pass 2 claims a fast path. A cross-class dispatcher
        /// clash stays where it is handled today (the export registry
        /// reports MS0020); the claim table only reserves the name. A
        /// containerless class also declares its module state as top-level
        /// variables (<see cref="IrModuleVariableExtractor.IsModuleState"/>),
        /// private ones included, so each declared variable name is reserved
        /// too and no fast path shadows it.
        /// </summary>
        private void ReserveNames(TypeFunctions shape)
        {
            foreach (var function in shape.Functions)
            {
                ClaimDispatcher(shape.Type, function);
            }

            if (!shape.Type.IsContainerless())
                return;

            foreach (var member in shape.Type.GetMembers())
            {
                if (IrModuleVariableExtractor.IsModuleState(member, TargetLanguage.TypeScript))
                {
                    Reserve(ModuleVariableValidator.EmittedName(member));
                }
            }
        }

        /// <summary>
        /// Pass 1, every static type: the name each static <c>[Import]</c>
        /// method binds in the files that call it (<see
        /// cref="SymbolHelper.ImportBindingOf"/>). A facade emits no function,
        /// so it binds its name wherever it is called — a class that keeps
        /// its container included — and a fast path of the same name would
        /// silently take the import's place.
        /// </summary>
        private void ReserveImportBindings(INamedTypeSymbol type)
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.ImportBindingOf(TargetLanguage.TypeScript) is { } binding)
                {
                    Reserve(binding);
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
            var dispatched = shape
                .Dispatched.GroupBy(function => function.Overload.MemberName)
                .SelectMany(group => group.OrderBy(function => function.Overload.Ordinal));

            foreach (var function in dispatched)
            {
                ClaimFastPath(shape.Type, function);
            }
        }

        private void ClaimDispatcher(INamedTypeSymbol type, MemberFunction function)
        {
            var dispatcher = IrToTsNamingPolicy.ToFunctionName(function.Member);

            Reserve(dispatcher);

            _claims[function.Member] = new MemberClaim(
                type.OriginalDefinition,
                function.Function.OverloadRef,
                new ClaimedNames(dispatcher, dispatcher)
            );
        }

        private void ClaimFastPath(INamedTypeSymbol type, DispatchedFunction function)
        {
            var claim = _claims[function.Member];
            var candidate = claim.Names.Dispatcher + function.Name.Suffix;
            var fastPath = TsFreeName.FirstFree(candidate, _taken.Contains);

            Reserve(fastPath);

            _claims[function.Member] = claim with
            {
                Names = claim.Names with { FastPath = fastPath },
            };

            if (fastPath == candidate)
                return;

            _findings.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.OverloadNameShadowed,
                    $"Fast path '{type.Name}.{candidate}' of '{function.SignatureText}' is already "
                        + $"taken in the barrel; it is emitted as '{fastPath}' (spec 013 FR-032).",
                    function.Member.Locations.FirstOrDefault()
                )
            );
        }

        private void Reserve(string name) => _taken.Add(name);

        private static Dictionary<IMethodSymbol, ClaimedNames> MembersOf(
            Dictionary<INamedTypeSymbol, Dictionary<IMethodSymbol, ClaimedNames>> byType,
            INamedTypeSymbol type
        )
        {
            if (byType.TryGetValue(type, out var members))
                return members;

            var created = new Dictionary<IMethodSymbol, ClaimedNames>(
                SymbolEqualityComparer.Default
            );

            byType[type] = created;

            return created;
        }

        /// <summary>
        /// A type's emitted module functions, each extracted from its own
        /// member, so a function never needs matching back to its member.
        /// </summary>
        internal sealed class TypeFunctions
        {
            private TypeFunctions(INamedTypeSymbol type, IReadOnlyList<MemberFunction> functions)
            {
                Type = type;
                Functions = functions;
            }

            public INamedTypeSymbol Type { get; }

            /// <summary>The emitted functions in member declaration order.</summary>
            public IReadOnlyList<MemberFunction> Functions { get; }

            /// <summary>The functions whose group dispatches, with their claim facts.</summary>
            public IEnumerable<DispatchedFunction> Dispatched =>
                Functions.Select(DispatchedFunction.Of).OfType<DispatchedFunction>();

            /// <summary>
            /// The shape of <paramref name="type"/>'s module functions —
            /// names, parameters and the overload claims, never bodies: the
            /// claims are a function of the declaration, and shape-only
            /// extraction reports nothing.
            /// </summary>
            public static TypeFunctions Extract(
                INamedTypeSymbol type,
                IrExtractionContext extraction
            )
            {
                using var _ = ExtractionDiagnostics.Suppress();

                var functions = type.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(member => member.IsEmittedModuleFunction(extraction.Target))
                    .Select(member => new MemberFunction(
                        member,
                        IrModuleFunctionExtractor.ExtractFunction(member, extraction)
                    ))
                    .ToList();

                return new TypeFunctions(type, functions);
            }
        }

        /// <summary>An emitted member beside the function extracted from it.</summary>
        internal sealed record MemberFunction(IMethodSymbol Member, IrModuleFunction Function);

        /// <summary>
        /// A function of a dispatched group: the overload identity, the
        /// fast-path suffix and the C# signature the IR recorded for it.
        /// </summary>
        internal sealed record DispatchedFunction(
            IMethodSymbol Member,
            IrOverloadRef Overload,
            IrOverloadName Name,
            string SignatureText
        )
        {
            public static DispatchedFunction? Of(MemberFunction function) =>
                function.Function
                    is {
                        OverloadRef: { } overload,
                        OverloadName: { } name,
                        SignatureText: { } signature,
                    }
                    ? new DispatchedFunction(function.Member, overload, name, signature)
                    : null;
        }

        /// <summary>
        /// The names one emitted member claimed, beside the overload identity
        /// a call site resolves it by (<c>null</c> when it stands alone).
        /// </summary>
        private sealed record MemberClaim(
            INamedTypeSymbol Type,
            IrOverloadRef? Overload,
            ClaimedNames Names
        );
    }
}

/// <summary>
/// The barrel names one exported module function claimed: its dispatcher
/// and its fast path, equal when the function stands alone.
/// </summary>
public sealed record ClaimedNames(string Dispatcher, string FastPath);
