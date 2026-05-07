using System.Diagnostics.CodeAnalysis;
using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Immutable shared state passed to every TypeScript-target transformer / builder during a
/// single transpilation run. Built once after the discovery + bootstrap phase of
/// <see cref="TypeTransformer.TransformAll"/> and read-only thereafter.
///
/// Holds:
/// <list type="bullet">
///   <item>The Roslyn <see cref="Compilation"/> + current assembly + assembly-wide transpile flag</item>
///   <item>The discovered transpilable type map (by both C# and TS name)</item>
///   <item>External import / BCL export / guard-name lookup tables</item>
///   <item>The <see cref="Transformation.PathNaming"/> helper carrying the project's root namespace</item>
///   <item>The <see cref="Transformation.DeclarativeMappingRegistry"/> with all <c>[MapMethod]</c>/<c>[MapProperty]</c> entries collected from referenced assemblies</item>
///   <item>A diagnostic reporter callback that drains into <c>TypeTransformer.Diagnostics</c></item>
/// </list>
///
/// Helpers and builders extracted from <see cref="TypeTransformer"/> take a single
/// <see cref="TypeScriptTransformContext"/> instead of growing parameter lists or
/// reaching into the transformer's private fields.
/// </summary>
public sealed class TypeScriptTransformContext(
    Compilation compilation,
    IAssemblySymbol? currentAssembly,
    bool assemblyWideTranspile,
    IReadOnlyDictionary<string, IrTranspilableTypeRef> transpilableTypes,
    IReadOnlyDictionary<string, IrExternalImport> externalImportMap,
    IReadOnlyDictionary<string, IrBclExport> bclExportMap,
    IReadOnlyDictionary<string, string> typeNamesBySymbol,
    IReadOnlySet<string> guardableTypeKeys,
    PathNaming pathNaming,
    DeclarativeMappingRegistry declarativeMappings,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    public Compilation Compilation { get; } = compilation;
    public IAssemblySymbol? CurrentAssembly { get; } = currentAssembly;
    public bool AssemblyWideTranspile { get; } = assemblyWideTranspile;

    /// <summary>
    /// Frontend-built projection of every current-assembly top-level
    /// transpilable type, indexed under both its C# source name and its
    /// TS alias. Resolves a bare identifier (walked out of the generated
    /// target AST) to <see cref="IrTranspilableTypeRef"/> emit metadata
    /// — origin key, namespace, on-disk file name, string-enum flag —
    /// without going back to the Roslyn symbol table. Consulted by the
    /// import collector (to decide whether to emit a local import) and
    /// the guard builder (to decide whether a field type has its own
    /// guard to recurse into).
    /// </summary>
    public IReadOnlyDictionary<string, IrTranspilableTypeRef> TranspilableTypes { get; } =
        transpilableTypes;

    /// <summary>
    /// Maps the camelCase emitted name of every transpilable extension
    /// helper (classic <c>(this T)</c> method, C# 14 <c>extension(T) { … }</c>
    /// member) to the <see cref="IrTranspilableTypeRef"/> of the static
    /// class that hosts it. The import collector queries this when a call
    /// site references a helper unqualified — the lowering pass drops the
    /// static-class prefix, so the resulting <c>squared(n)</c> needs to
    /// resolve back to its owning file (<c>./int-ext</c>) at import time.
    /// Built lazily on first access from <see cref="Compilation"/> against
    /// <see cref="TranspilableTypes"/> so the registry stays in sync with
    /// the project's emitted-file layout.
    /// </summary>
    public IReadOnlyDictionary<string, IrTranspilableTypeRef> ExtensionHelperFunctions =>
        _extensionHelperFunctions ??= BuildExtensionHelperFunctions();

    private IReadOnlyDictionary<string, IrTranspilableTypeRef>? _extensionHelperFunctions;

    private IReadOnlyDictionary<string, IrTranspilableTypeRef> BuildExtensionHelperFunctions()
    {
        var map = new Dictionary<string, IrTranspilableTypeRef>(StringComparer.Ordinal);
        var firstClaim = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        if (CurrentAssembly is null)
            return map;

        foreach (var type in EnumerateTopLevelStaticTypes(CurrentAssembly.GlobalNamespace))
        {
            if (!TranspilableTypes.TryGetValue(type.Name, out var ownerRef))
                continue;

            foreach (var member in type.GetMembers())
                RegisterStaticClassMember(member, ownerRef, map, firstClaim, ReportDiagnostic);
        }

        return map;
    }

    private static void RegisterStaticClassMember(
        ISymbol member,
        IrTranspilableTypeRef ownerRef,
        Dictionary<string, IrTranspilableTypeRef> map,
        Dictionary<string, ISymbol> firstClaim,
        Action<MetanoDiagnostic> reportDiagnostic
    )
    {
        switch (member)
        {
            case IMethodSymbol method
                when method.IsExtensionMethod
                    && method.MethodKind is MethodKind.Ordinary
                    && method.DeclaredAccessibility == Accessibility.Public:
                Register(
                    ResolveMethodHelperName(method),
                    method,
                    ownerRef,
                    map,
                    firstClaim,
                    reportDiagnostic
                );
                break;

            // Classic extension property: Roslyn surfaces it as an
            // `IPropertySymbol` on a static class with the receiver in
            // `Parameters[0]`. The call-site rewrite lowers reads to
            // `prop$get(receiver)` so the registry must resolve those
            // helpers for cross-file imports.
            case IPropertySymbol prop
                when prop.DeclaredAccessibility == Accessibility.Public
                    && !prop.IsIndexer
                    && prop.Parameters.Length > 0:
                Register(
                    ResolvePropertyHelperName(prop),
                    prop,
                    ownerRef,
                    map,
                    firstClaim,
                    reportDiagnostic
                );
                break;

            case INamedTypeSymbol nested
                when string.IsNullOrEmpty(nested.Name)
                    && nested.ContainingType is { IsStatic: true }:
                foreach (var nestedMember in nested.GetMembers())
                    RegisterExtensionBlockMember(
                        nestedMember,
                        ownerRef,
                        map,
                        firstClaim,
                        reportDiagnostic
                    );
                break;
        }
    }

    private static void RegisterExtensionBlockMember(
        ISymbol member,
        IrTranspilableTypeRef ownerRef,
        Dictionary<string, IrTranspilableTypeRef> map,
        Dictionary<string, ISymbol> firstClaim,
        Action<MetanoDiagnostic> reportDiagnostic
    )
    {
        switch (member)
        {
            case IMethodSymbol method
                when method.MethodKind is MethodKind.Ordinary
                    && method.DeclaredAccessibility == Accessibility.Public:
                Register(
                    ResolveMethodHelperName(method),
                    method,
                    ownerRef,
                    map,
                    firstClaim,
                    reportDiagnostic
                );
                break;

            case IPropertySymbol prop
                when prop.DeclaredAccessibility == Accessibility.Public && !prop.IsIndexer:
                Register(
                    ResolvePropertyHelperName(prop),
                    prop,
                    ownerRef,
                    map,
                    firstClaim,
                    reportDiagnostic
                );
                break;
        }
    }

    /// <summary>
    /// Mirrors the same <c>[Name]</c> resolution the IR call-site rewrite
    /// applies, so the registry key and the lowered call agree on the
    /// emitted helper name. Falls back to camelCase when no override is
    /// declared, matching <c>IrToTsNamingPolicy.ToFunctionName</c>.
    /// </summary>
    private static string ResolveMethodHelperName(IMethodSymbol method) =>
        SymbolHelper.GetNameOverride(method, TargetLanguage.TypeScript)
        ?? TypeScriptNaming.ToCamelCase(method.Name);

    private static string ResolvePropertyHelperName(IPropertySymbol property) =>
        (
            SymbolHelper.GetNameOverride(property, TargetLanguage.TypeScript)
            ?? TypeScriptNaming.ToCamelCase(property.Name)
        ) + IrExtensionConventions.PropertyGetterSuffix;

    /// <summary>
    /// Records <paramref name="ownerRef"/> as the import target for
    /// <paramref name="helperName"/>, or raises <c>MS0021</c> when a
    /// different static class already claimed the same emitted name.
    /// First-write wins so the previously-registered import keeps
    /// flowing; the user resolves the clash by adding <c>[Name]</c> on
    /// either side.
    /// </summary>
    private static void Register(
        string helperName,
        ISymbol member,
        IrTranspilableTypeRef ownerRef,
        Dictionary<string, IrTranspilableTypeRef> map,
        Dictionary<string, ISymbol> firstClaim,
        Action<MetanoDiagnostic> reportDiagnostic
    )
    {
        if (firstClaim.TryGetValue(helperName, out var prior))
        {
            if (SymbolEqualityComparer.Default.Equals(prior.ContainingType, member.ContainingType))
                return;
            // Roslyn surfaces C# 14 `extension(R r) { … }` members twice: once
            // inside a synthetic empty-name nested type and once lifted onto
            // the static class. Same source — collapse to one registration.
            if (AreSameLogicalExtensionMember(prior, member))
                return;
            var priorOwner = prior.ContainingType?.Name ?? "<global>";
            var newOwner = member.ContainingType?.Name ?? "<global>";
            reportDiagnostic(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.ExtensionHelperNameClash,
                    $"Extension helper '{newOwner}.{member.Name}' resolves to TS export "
                        + $"name '{helperName}', which is already exported by "
                        + $"'{priorOwner}.{prior.Name}'. Add a [Name(\"...\")] override on "
                        + "one of them so the import collector can pick the right module.",
                    member.Locations.FirstOrDefault()
                )
            );
            return;
        }
        map[helperName] = ownerRef;
        firstClaim[helperName] = member;
    }

    private static bool AreSameLogicalExtensionMember(ISymbol a, ISymbol b)
    {
        if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal))
            return false;
        var aOwner = a.ContainingType;
        var bOwner = b.ContainingType;
        if (aOwner is null || bOwner is null)
            return false;
        var aLogical = string.IsNullOrEmpty(aOwner.Name) ? aOwner.ContainingType : aOwner;
        var bLogical = string.IsNullOrEmpty(bOwner.Name) ? bOwner.ContainingType : bOwner;
        return SymbolEqualityComparer.Default.Equals(aLogical, bLogical);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTopLevelStaticTypes(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (var nested in EnumerateTopLevelStaticTypes(ns))
                        yield return nested;
                    break;

                case INamedTypeSymbol { IsStatic: true } type:
                    yield return type;
                    break;
            }
        }
    }

    public IReadOnlyDictionary<string, IrExternalImport> ExternalImportMap { get; } =
        externalImportMap;
    public IReadOnlyDictionary<string, IrBclExport> BclExportMap { get; } = bclExportMap;
    public IReadOnlyDictionary<string, string> TypeNamesBySymbol { get; } = typeNamesBySymbol;
    public IReadOnlySet<string> GuardableTypeKeys { get; } = guardableTypeKeys;
    public PathNaming PathNaming { get; } = pathNaming;
    public DeclarativeMappingRegistry DeclarativeMappings { get; } = declarativeMappings;
    public Action<MetanoDiagnostic> ReportDiagnostic { get; } = reportDiagnostic;

    /// <summary>
    /// Per-export lookup for <c>[NoContainer]</c> static methods: the
    /// emitted (camelCase) function name maps back to its declaring
    /// type's <see cref="IrTranspilableTypeRef"/>. Populated by
    /// <see cref="TypeTransformer.TransformAll"/> after type discovery
    /// and consumed by <see cref="ImportCollector"/> so a flattened
    /// cross-module call site (<c>column(args)</c> referencing a
    /// function emitted in <c>mvu/ui.ts</c>) collects the right
    /// <c>import { column } from "./ui"</c> line. Without this map the
    /// collector — which keys imports off type names — cannot resolve
    /// a bare lowercase identifier back to a transpilable origin.
    /// </summary>
    public IReadOnlyDictionary<
        string,
        NoContainerExport
    > NoContainerFunctionExports { get; init; } =
        new Dictionary<string, NoContainerExport>(StringComparer.Ordinal);

    /// <summary>
    /// Per-source-file synthesized alias map keyed by the absolute file
    /// path. Populated when <c>BuildNoContainerFunctionExports</c> detects a
    /// factory shadowing an imported transpilable type and falls back to
    /// auto-aliasing (Stage 2 of #181). Consumed by
    /// <c>TransformGroup</c> when seeding <c>UsingAliasScope</c> so the
    /// synthesized alias flows through every emit site exactly the way a
    /// user-declared <c>using X = Y;</c> would.
    /// </summary>
    public IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > SynthesizedAliasesByFile { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>
    /// Per-source-file alias map produced by <c>[ImportAlias]</c>
    /// attributes on file-scoped <c>file class TsModule</c> carriers.
    /// Built lazily once per context from the live <c>Compilation</c> so
    /// the registry stays in sync with the source tree on every transform
    /// pass. Layer B in #181's two-layer model: takes precedence over the
    /// user's <c>using</c> aliases (Layer A) and the auto-synthesized
    /// fallback (Stage 2).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ImportAliasOverrides =>
        _importAliasOverrides ??= ImportAliasResolver.BuildPerFileAliases(
            Compilation,
            TargetLanguage.TypeScript
        );

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _importAliasOverrides;

    /// <summary>
    /// Recognizes a referenced identifier as the TypeScript guard
    /// function for a transpilable type — i.e. an <c>is{Name}</c>
    /// import where the underlying type is in
    /// <see cref="GuardableTypeKeys"/>. Returns the guarded type's
    /// IR projection so callers can compute file paths / namespaces
    /// without re-looking it up. The <c>is</c> prefix is the TypeScript
    /// naming convention and stays target-local; the IR ships only the
    /// guardable-type set.
    /// </summary>
    public bool TryResolveGuardImport(
        string candidate,
        [NotNullWhen(true)] out IrTranspilableTypeRef? guarded
    )
    {
        guarded = null;
        if (!candidate.StartsWith("is", StringComparison.Ordinal) || candidate.Length <= 2)
            return false;
        var guessedTsName = candidate[2..];
        if (!TranspilableTypes.TryGetValue(guessedTsName, out var resolved))
            return false;
        if (!GuardableTypeKeys.Contains(resolved.Key))
            return false;
        guarded = resolved;
        return true;
    }

    /// <summary>
    /// Resolves the target-facing TypeScript name for a Roslyn type
    /// symbol without needing a constructed
    /// <see cref="TypeScriptTransformContext"/> instance. Used during
    /// the early setup phase of <c>TypeTransformer.TransformAll</c>
    /// where the context does not yet exist; in every other call site
    /// prefer the instance method so future changes stay in one place.
    /// </summary>
    internal static string ResolveTsName(
        IReadOnlyDictionary<string, string>? typeNamesBySymbol,
        INamedTypeSymbol type
    ) =>
        typeNamesBySymbol is not null
        && typeNamesBySymbol.TryGetValue(type.GetCrossAssemblyOriginKey(), out var name)
            ? name
            : type.Name;

    /// <summary>
    /// Resolves the target-facing TypeScript name for a Roslyn type symbol.
    /// Reads the frontend-populated <see cref="TypeNamesBySymbol"/> dictionary
    /// so <c>[Name(TypeScript, …)]</c> overrides are honored; falls back to
    /// <see cref="ISymbol.Name"/> for BCL types and anything the frontend did
    /// not precompute (mirrors the legacy <c>TypeTransformer.GetTsTypeName</c>
    /// contract that this helper replaces).
    /// </summary>
    public string ResolveTsName(INamedTypeSymbol type) => ResolveTsName(TypeNamesBySymbol, type);

    /// <summary>
    /// Reports MS0001 (UnsupportedFeature) for an IR-pipeline body the bridge
    /// can't lower. Used as the graceful-failure signal from
    /// <c>IrToTsClassEmitter</c> and <c>TypeTransformer</c> when the legacy
    /// fallbacks are gone but the IR coverage probe still rejects the body —
    /// surfaces the gap at build time instead of crashing or silently dropping
    /// output.
    /// </summary>
    public void ReportUnsupportedBody(ISymbol contextSymbol, string message) =>
        ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                message,
                contextSymbol.Locations.FirstOrDefault()
            )
        );

    /// <summary>
    /// The per-compilation type mapping context. Provides explicit access to the mutable
    /// state that <see cref="TypeMapper"/> needs during transformation, replacing the
    /// legacy <c>[ThreadStatic]</c> fields.
    /// </summary>
    public TypeMappingContext? TypeMapping { get; init; }

    /// <summary>
    /// Switches method-body lowering between the IR pipeline (default,
    /// <c>true</c>) and a no-op stub used by tests that want to verify the IR
    /// path is the single source of truth. With the legacy expression/dispatcher
    /// transformers removed, flipping this to <c>false</c> causes constructor
    /// and overload dispatchers to throw — there is no longer-existing fallback
    /// to fall through to.
    /// </summary>
    public bool UseIrBodiesWhenCovered { get; init; } = true;

    private BclExportTypeOverrides? _bclOverrides;

    /// <summary>
    /// Shared <see cref="IrToTsTypeOverrides"/> that applies <c>[ExportFromBcl]</c>
    /// mappings (decimal → Decimal from decimal.js, etc.) when lowering an
    /// <see cref="IrTypeRef"/> through <see cref="IrToTsTypeMapper"/>. Tracks
    /// per-package usage in <see cref="TypeMappingContext.UsedCrossPackages"/>
    /// so the CLI driver emits the right <c>package.json#dependencies</c>.
    /// Created once per compilation and reused across every emitter / bridge /
    /// builder that lowers type refs.
    /// </summary>
    public BclExportTypeOverrides BclOverrides =>
        _bclOverrides ??= new BclExportTypeOverrides(
            TypeMapping!.BclExportMap,
            TypeMapping.UsedCrossPackages
        );

    private IrTypeOriginResolver? _originResolver;

    /// <summary>
    /// Shared <see cref="IrTypeOriginResolver"/> that records cross-assembly
    /// type origins + drains cross-package misses into the current
    /// <see cref="TypeMappingContext"/>. Created once per compilation so the
    /// closure isn't rebuilt on every <see cref="IrTypeRefMapper.Map"/> call.
    /// </summary>
    public IrTypeOriginResolver OriginResolver =>
        _originResolver ??= IrTypeOriginResolverFactory.Create(TypeMapping!);
}

/// <summary>
/// Bundle keyed by a flattened <c>[NoContainer]</c> function name (the
/// camelCased member identifier the consumer file references). Carries
/// the owning type's transpilable shape (namespace / file name) for
/// own-assembly resolution and an optional <see cref="TsTypeOrigin"/>
/// for cross-assembly entries — the latter routes the consumer's import
/// line through the originating <c>[EmitPackage]</c> instead of a
/// project-relative path.
/// </summary>
public sealed record NoContainerExport(
    IrTranspilableTypeRef Owner,
    TsTypeOrigin? CrossPackageOrigin = null
);
