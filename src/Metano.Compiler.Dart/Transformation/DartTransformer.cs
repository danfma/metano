using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Compiler.Dart.AST;
using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Dart.Transformation;

/// <summary>
/// Orchestrates Dart emission: reads the ordered transpilable-type list
/// off the shared <see cref="IrCompilation"/>, extracts each into IR,
/// routes to the appropriate Dart bridge, and assembles the resulting
/// <see cref="DartSourceFile"/>s with their imports.
/// <para>
/// This is a prototype — currently handles enums, interfaces, and plain class shapes
/// (no method bodies). Records/operators/exceptions need expression extraction, which
/// lands in Phase 5.
/// </para>
/// </summary>
public sealed class DartTransformer
{
    private readonly IrCompilation _ir;
    private readonly Compilation _compilation;
    private readonly List<MetanoDiagnostic> _diagnostics = new();
    private readonly object _diagnosticsLock = new();
    private readonly DartOverloadClaimsRegistry _overloadClaims;
    private readonly DartIrRewriter _rewriter;

    public DartTransformer(IrCompilation ir, Compilation compilation)
    {
        _ir = ir;
        _compilation = compilation;
        _overloadClaims = new DartOverloadClaimsRegistry(compilation, Extraction);

        _rewriter = new DartIrRewriter(
            DeclarativeMappingRegistry.FromIr(ir),
            ContainerlessTypeNames(ir),
            _overloadClaims
        );
    }

    /// <summary>
    /// The extraction context the core extractors receive: this compilation
    /// and the Dart target, no local-naming decision — local-name collision
    /// renaming is TypeScript-only until #402 lands it for Dart. Dart always
    /// takes the exact 64-bit lowering: its <c>int</c> is 64-bit, so the
    /// conversions it prints are the narrowing ones (spec 012 DECIDED-19).
    /// A dispatched constructor set becomes named constructors, so a
    /// construction binds the overload it selects; an overloaded operator
    /// group keeps one <c>operator</c> declaration, so Dart leaves
    /// <see cref="IrExtractionContext.EmitsOperatorFastPaths"/> unset and an
    /// operator application keeps the source operator (spec 013 FR-022).
    /// </summary>
    private IrExtractionContext Extraction =>
        new(Compilation: _compilation, Target: TargetLanguage.Dart)
        {
            ExactInt64Lowering = true,
            EmitsConstructorSelectors = DartTypeShapes.EmitsNamedConstructors,
        };

    public IReadOnlyList<MetanoDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Thread-safe sink for diagnostic emission. Mirrors the TypeScript
    /// transformer's sink contract (#218 / #226): every producer routes
    /// through this single locked entry-point so a future parallel-Dart
    /// sweep does not corrupt the list. Open <c>ExtractionDiagnostics</c>
    /// against this method instead of <c>_diagnostics.Add</c>.
    /// </summary>
    internal void ReportDiagnostic(MetanoDiagnostic diagnostic)
    {
        lock (_diagnosticsLock)
            _diagnostics.Add(diagnostic);
    }

    public IReadOnlyList<DartSourceFile> TransformAll()
    {
        // Body extraction (which spawns the queryable-tree walker)
        // runs here — open the MS0024 sink (#218) so reports land
        // in this transformer's diagnostics list and surface via
        // the host's standard merge.
        using var extractionDiagnosticSink = Metano.Compiler.Extraction.ExtractionDiagnostics.Open(
            ReportDiagnostic
        );

        var files = new List<DartSourceFile>();
        var transpilable = DiscoverTranspilableTypes();

        // Map of emitted type name (PascalCase) → file name (snake_case.dart) so bridges
        // can emit relative imports for same-package references. Duplicate simple names
        // across namespaces (e.g., Admin.User and Billing.User) are currently unsupported
        // because the file name derives from the simple name alone; we surface a
        // diagnostic and keep the first occurrence so generation still completes for the
        // rest of the project. Proper namespace-qualified output paths are tracked as
        // a follow-up.
        var localTypeFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in transpilable)
        {
            // [Ignore(TargetLanguage.Dart)] declares the type for C# use but
            // skips Dart file emission. It must not make it into the local
            // import map either — otherwise a consumer referencing the type
            // would emit `import 'foo.dart';` pointing at a file that never
            // got written, breaking the build.
            if (SymbolHelper.HasIgnore(type, TargetLanguage.Dart))
                continue;

            var emittedName = GetEmittedTypeName(type);
            var fileName = IrToDartNamingPolicy.ToFileName(emittedName);
            if (localTypeFiles.ContainsKey(emittedName))
            {
                ReportDiagnostic(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.AmbiguousConstruct,
                        $"Dart target: two transpilable types share the simple name "
                            + $"'{emittedName}' (declared in different namespaces). The "
                            + $"current Dart backend derives the output file from the "
                            + $"simple name and cannot disambiguate. Keep a single "
                            + $"'{emittedName}' or add an [EmitInFile] / [Name] "
                            + $"attribute to rename one of them."
                    )
                );
                continue;
            }
            localTypeFiles[emittedName] = fileName;
            seenTypes.Add(type);
        }

        foreach (var type in transpilable)
        {
            if (SymbolHelper.HasIgnore(type, TargetLanguage.Dart))
                continue;
            // A duplicate simple name already surfaced a diagnostic; skip the
            // late-arriving sibling so we don't produce two files targeting the
            // same path (the OS would pick last-write-wins and the build graph
            // would silently lose one).
            if (!seenTypes.Contains(type))
                continue;

            var fileName = IrToDartNamingPolicy.ToFileName(GetEmittedTypeName(type));
            var statements = new List<DartTopLevel>();
            var runtimeRequirements = new HashSet<IrRuntimeRequirement>();

            switch (type.TypeKind)
            {
                case TypeKind.Enum:
                    var enumIr = IrEnumExtractor.Extract(type, Extraction);
                    IrToDartEnumBridge.Convert(enumIr, statements);
                    ScanInto(runtimeRequirements, enumIr);
                    break;

                case TypeKind.Interface:
                    var interfaceIr = IrInterfaceExtractor.Extract(type, Extraction);
                    IrToDartInterfaceBridge.Convert(interfaceIr, ClaimsOf(type), statements);
                    ScanInto(runtimeRequirements, interfaceIr);
                    break;

                case TypeKind.Class:
                case TypeKind.Struct:
                    // Static classes decorated with [NoContainer] (or the legacy
                    // [ExportedAsModule] still tracked under the same predicate)
                    // collapse to top-level Dart functions (the idiomatic
                    // utility-module shape) instead of a class of static methods.
                    if (DartTypeShapes.LowersToTopLevelFunctions(type))
                    {
                        var functions = IrModuleFunctionExtractor.Extract(type, Extraction);

                        IrToDartModuleBridge.Convert(
                            functions,
                            ClaimsOf(type),
                            statements,
                            _rewriter
                        );

                        ScanFunctionsInto(runtimeRequirements, functions);
                        break;
                    }

                    var classIr = IrClassExtractor.Extract(type, Extraction);
                    var classClaims = ClaimsOf(type);

                    ReportUnnamedOverloads(classIr, type.Name, classClaims.Shape);
                    ReportMultiplyConstructedInterfaces(type);
                    ReportChainedConstructorsWithBodies(type, classIr, classClaims.Shape);
                    IrToDartClassBridge.Convert(classIr, classClaims, statements, _rewriter);
                    ScanInto(runtimeRequirements, classIr);
                    break;

                case TypeKind.Delegate:
                    var delegateIr = IrDelegateExtractor.Extract(type, Extraction);

                    if (delegateIr is null)
                    {
                        ReportDiagnostic(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Warning,
                                DiagnosticCodes.UnsupportedFeature,
                                $"Dart target: failed to extract delegate IR for '{type.Name}'."
                            )
                        );
                        continue;
                    }

                    IrToDartDelegateBridge.Convert(delegateIr, statements);
                    ScanInto(runtimeRequirements, delegateIr);
                    break;

                default:
                    ReportDiagnostic(
                        new MetanoDiagnostic(
                            MetanoDiagnosticSeverity.Warning,
                            DiagnosticCodes.UnsupportedFeature,
                            $"Dart target: type kind '{type.TypeKind}' for '{type.Name}' "
                                + "is not yet supported."
                        )
                    );
                    continue;
            }

            var imports = DartImportCollector.Collect(
                statements,
                fileName,
                localTypeFiles,
                runtimeRequirements
            );
            statements.InsertRange(0, imports);
            files.Add(new DartSourceFile(fileName, statements));
        }

        return files;
    }

    /// <summary>
    /// The claim results of a type this transform emits, with their findings
    /// (<c>MS0067</c>, <c>MS0068</c>) reported at its declarations — once,
    /// since each type is emitted once.
    /// </summary>
    private DartOverloadClaims ClaimsOf(INamedTypeSymbol type)
    {
        var claims = _overloadClaims.ClaimsOf(type);

        claims.Report(type, ReportDiagnostic);

        return claims;
    }

    /// <summary>
    /// <c>MS0069</c> at a type that implements a generic interface with
    /// overload groups more than once: Dart declares each root name once.
    /// </summary>
    private void ReportMultiplyConstructedInterfaces(INamedTypeSymbol type)
    {
        var diagnostics = MultiplyConstructedInterfaces.DiagnosticsOf(type, TargetLanguage.Dart);

        foreach (var diagnostic in diagnostics)
            ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// <c>MS0001</c> at each constructor of a dispatched set that chains
    /// through <c>: this(...)</c> and has a body of its own
    /// (<see cref="IrToDartClassBridge.KeepsBodyWithoutChain"/>): Dart's
    /// redirecting constructors have no body, so its named constructor keeps
    /// the body without the chain, and the members the chained constructor
    /// sets stay unset.
    /// </summary>
    private void ReportChainedConstructorsWithBodies(
        INamedTypeSymbol type,
        IrClassDeclaration classIr,
        DartOverloadShape shape
    )
    {
        if (!shape.NamesMembers || classIr.Constructor is not { Overloads.Count: > 0 } constructor)
            return;

        var chainedWithBodies = constructor.DeclarationsInOrder.Where(
            IrToDartClassBridge.KeepsBodyWithoutChain
        );

        foreach (var overload in chainedWithBodies)
        {
            var site = new OverloadDeclarationSite(
                IrOverloadRef.ConstructorName,
                overload.DeclarationOrdinal
            );

            ReportDiagnostic(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"Dart target: the constructor '{overload.SignatureText}' chains through "
                        + ": this(...) and has a body; a Dart redirecting constructor has none, so "
                        + "its named constructor keeps the body without the chain.",
                    site.LocationIn(type)
                )
            );
        }
    }

    /// <summary>
    /// The stable full names of the transpilable static classes that
    /// <see cref="DartTypeShapes.LowersToTopLevelFunctions"/> flattens. Types of referenced
    /// assemblies are not listed, so their call sites keep the qualifier.
    /// </summary>
    private static HashSet<string> ContainerlessTypeNames(IrCompilation ir) =>
        (ir.TranspilableTypeEntries ?? [])
            .Select(entry => entry.Symbol)
            .Where(DartTypeShapes.LowersToTopLevelFunctions)
            .Select(type => type.GetStableFullName())
            .ToHashSet(StringComparer.Ordinal);

    // ── Discovery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens the frontend-owned ordered top-level transpilable-type
    /// list (<see cref="IrCompilation.TranspilableTypeEntries"/>) plus
    /// every nested transpilable type under each entry into a single
    /// emission list. The Dart prototype emits one file per type (no
    /// companion-namespace wrapping like the TS target), so nested types
    /// join the flat list here — <see cref="CollectNested"/> recurses
    /// into them using the frontend-reported
    /// <see cref="IrCompilation.AssemblyWideTranspile"/> flag.
    /// </summary>
    private List<INamedTypeSymbol> DiscoverTranspilableTypes()
    {
        var entries = _ir.TranspilableTypeEntries ?? Array.Empty<IrTranspilableTypeEntry>();
        var result = new List<INamedTypeSymbol>();
        foreach (var entry in entries)
        {
            result.Add(entry.Symbol);
            CollectNested(entry.Symbol, _ir.AssemblyWideTranspile, result);
        }
        return result;
    }

    /// <summary>
    /// Dart has no method overloading: a dispatched group is declared as one
    /// fast path per overload (spec 013 FR-022). Two kinds of overload set
    /// keep the primary declaration only — an overloaded operator group, since
    /// Dart declares one <c>operator</c> per symbol, and the overloads of a
    /// keep-today's-emission shape (<see cref="DartOverloadShape.NamesMembers"/>);
    /// silently dropping the others would hide a real semantic mismatch.
    /// </summary>
    private void ReportUnnamedOverloads(
        IrClassDeclaration classIr,
        string typeName,
        DartOverloadShape shape
    )
    {
        var namesOverloadMembers = shape.NamesMembers;

        foreach (var member in classIr.Members ?? [])
        {
            if (member is not IrMethodDeclaration { Overloads.Count: > 0 } method)
                continue;

            if (namesOverloadMembers && DartClassMemberNames.IsFastPathGroup(method))
                continue;

            ReportDiagnostic(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"Dart target: '{typeName}.{method.Name}' has "
                        + $"{method.Overloads!.Count + 1} overloads but Dart doesn't "
                        + "support overloading it. Emitting the primary signature "
                        + "only; rename the other overloads via [Name(\"...\")] or merge "
                        + "them into a single method with optional parameters."
                )
            );
        }
    }

    private static void CollectNested(
        INamedTypeSymbol type,
        bool assemblyWide,
        List<INamedTypeSymbol> acc
    )
    {
        foreach (var nested in type.GetTypeMembers())
        {
            if (IsTranspilable(nested, assemblyWide))
                acc.Add(nested);
            CollectNested(nested, assemblyWide, acc);
        }
    }

    private static bool IsTranspilable(INamedTypeSymbol type, bool assemblyWide)
    {
        if (SymbolHelper.HasIgnore(type))
            return false;
        if (SymbolHelper.HasTranspile(type))
            return true;
        if (assemblyWide && type.DeclaredAccessibility == Accessibility.Public)
            return true;
        return false;
    }

    private static string GetEmittedTypeName(INamedTypeSymbol type) =>
        SymbolHelper.GetNameOverride(type, TargetLanguage.Dart) ?? type.Name;

    // ── Runtime requirements ──────────────────────────────────────────────

    private static void ScanInto(HashSet<IrRuntimeRequirement> sink, IrTypeDeclaration declaration)
    {
        foreach (var req in IrRuntimeRequirementScanner.Scan(declaration))
            sink.Add(req);
    }

    /// <summary>
    /// Scans module-level functions for runtime requirements. Wraps the function
    /// list in a synthetic <see cref="IrModule"/> so the shared scanner — which
    /// only operates on type declarations or whole modules — can walk them.
    /// Tracking issue: add an <c>IrRuntimeRequirementScanner.Scan(IReadOnlyList&lt;IrModuleFunction&gt;)</c>
    /// overload so callers don't fabricate IR shells just to satisfy the entry point.
    /// </summary>
    private static void ScanFunctionsInto(
        HashSet<IrRuntimeRequirement> sink,
        IReadOnlyList<IrModuleFunction> functions
    )
    {
        var module = new IrModule(
            Name: string.Empty,
            Namespace: null,
            Types: Array.Empty<IrTypeDeclaration>(),
            Functions: functions
        );
        foreach (var req in IrRuntimeRequirementScanner.Scan(module))
            sink.Add(req);
    }
}
