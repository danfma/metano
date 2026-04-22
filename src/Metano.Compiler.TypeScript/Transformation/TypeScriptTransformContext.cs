using System.Diagnostics.CodeAnalysis;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Transformation;

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

    public IReadOnlyDictionary<string, IrExternalImport> ExternalImportMap { get; } =
        externalImportMap;
    public IReadOnlyDictionary<string, IrBclExport> BclExportMap { get; } = bclExportMap;
    public IReadOnlyDictionary<string, string> TypeNamesBySymbol { get; } = typeNamesBySymbol;
    public IReadOnlySet<string> GuardableTypeKeys { get; } = guardableTypeKeys;
    public PathNaming PathNaming { get; } = pathNaming;
    public DeclarativeMappingRegistry DeclarativeMappings { get; } = declarativeMappings;
    public Action<MetanoDiagnostic> ReportDiagnostic { get; } = reportDiagnostic;

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
