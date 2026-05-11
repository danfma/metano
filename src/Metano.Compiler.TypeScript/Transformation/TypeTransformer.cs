using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Compiler.Caching;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Metano.Compiler.TypeScript.Caching;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Transforms C# types annotated with [Transpile] into TypeScript AST source files.
/// Receives both the shared <see cref="IrCompilation"/> (canonical input from the
/// active source frontend) and the underlying Roslyn <see cref="Compilation"/> the
/// legacy bridges still walk. Fields the frontend already populates are read off
/// the IR; everything else falls back to Roslyn during the incremental migration.
/// </summary>
public sealed class TypeTransformer(IrCompilation ir, Compilation compilation)
{
    private readonly List<MetanoDiagnostic> _diagnostics = [];

    /// <summary>
    /// Guards <see cref="_diagnostics"/> against parallel writes from the
    /// per-group transformation loop (#21 — parallel TypeTransformer).
    /// Adds happen sparsely (one per diagnostic-worthy event), so a
    /// simple lock keeps order stable without the overhead of swapping
    /// the backing list to a concurrent collection.
    /// </summary>
    private readonly object _diagnosticsLock = new();

    /// <summary>
    /// Forwarded to <see cref="TypeScriptTransformContext.UseIrBodiesWhenCovered"/>.
    /// Always <c>true</c> in production — the IR pipeline is the only path for
    /// method-body lowering now that the legacy transformers are gone. Kept as
    /// an init-only kill switch so a regression test can intentionally bypass
    /// the IR bridges and confirm the type-emission code skips the type
    /// without crashing.
    /// </summary>
    public bool UseIrBodiesWhenCovered { get; init; } = true;

    /// <summary>
    /// Forwarded to <see cref="BarrelFileGenerator.Generate"/>. When
    /// <c>true</c>, the generator emits <c>src/index.ts</c> mirroring
    /// the C# namespace hierarchy via <c>export namespace</c> blocks
    /// (opt-in; see ADR-0006 + issue #22).
    /// </summary>
    public bool NamespaceBarrels { get; init; }

    /// <summary>
    /// When <c>true</c>, <see cref="ApplyInterfacePrefixStrip"/>
    /// rewrites every interface entry in
    /// <see cref="IrCompilation.TypeNamesBySymbol"/> whose C# name
    /// matches <c>^I[A-Z]</c>, dropping the leading <c>I</c>.
    /// Collisions with sibling types in the same namespace keep the
    /// prefix and raise <c>MS0017</c>. Explicit
    /// <c>[Name(TypeScript, "…")]</c> overrides win over the strip.
    /// Opt-in via <c>--strip-interface-prefix</c>.
    /// </summary>
    public bool StripInterfacePrefix { get; init; }

    /// <summary>
    /// Output directory for the active run, supplied by the host
    /// (<see cref="Metano.Compiler.TranspilerHost"/>) so the per-group
    /// cache (PR 3c) can live next to the generated <c>.ts</c> files.
    /// <c>null</c> disables the per-group skip path — useful for unit
    /// tests that drive the transformer with no on-disk side effects.
    /// </summary>
    public string? CacheOutputDir { get; init; }

    /// <summary>
    /// File-prefix block applied by the host at emit time. When set,
    /// per-group cache hits strip it from the disk content before
    /// re-publishing — the host re-applies the prefix on the next
    /// write so storing the prefixed form would double it.
    /// </summary>
    public string? CacheFilePrefix { get; init; }

    /// <summary>
    /// On a per-group cache hit (PR 3c) the transformer reuses the
    /// content already on disk: stub <c>TsSourceFile</c>s flow
    /// through barrel + cyclic detection, but the printed output for
    /// those paths comes straight from disk so the next emit
    /// pass writes the same bytes back instead of corrupting the
    /// real file with the stub's empty body.
    /// </summary>
    public IReadOnlyDictionary<string, string> CachedFileContents => _cachedFileContents;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        string
    > _cachedFileContents = new(StringComparer.Ordinal);

    /// <summary>
    /// Diagnostics collected during transformation. Includes warnings about unsupported
    /// language features and other issues that the user should know about.
    /// </summary>
    public IReadOnlyList<MetanoDiagnostic> Diagnostics => _diagnostics;

    private readonly Dictionary<string, string> _crossPackageDependencies = new();

    /// <summary>
    /// Maps each cross-package npm name that was actually referenced during
    /// transformation to its npm version specifier (<c>^Major.Minor.Patch</c>, or
    /// <c>workspace:*</c> when the source assembly has no explicit version). Drained
    /// from <see cref="TypeMappingContext.UsedCrossPackages"/> at the end of <c>TransformAll</c>
    /// and surfaced to the CLI driver so the package.json writer can merge the entries
    /// into <c>dependencies</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> CrossPackageDependencies =>
        _crossPackageDependencies;

    internal void ReportDiagnostic(MetanoDiagnostic diagnostic)
    {
        lock (_diagnosticsLock)
            _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Thread-safe sink for the per-group transformation loop. Wraps
    /// <see cref="_diagnostics"/> writes inside the per-group lock so
    /// parallel workers serialise on diagnostic emission.
    /// </summary>
    private void AddDiagnostic(MetanoDiagnostic diagnostic)
    {
        lock (_diagnosticsLock)
            _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Processes nested types of <paramref name="parent"/> and adds them as a companion
    /// namespace declaration to <paramref name="statements"/>. This leverages TS declaration
    /// merging so that `Outer.Inner` access syntax works just like in C#.
    /// </summary>
    private void TransformNestedTypes(INamedTypeSymbol parent, List<TsTopLevel> statements)
    {
        var nested = parent
            .GetTypeMembers()
            .Where(t => !t.IsImplicitlyDeclared)
            .Where(t => t.DeclaredAccessibility != Accessibility.Internal)
            .Where(t =>
                SymbolHelper.IsTranspilable(
                    t,
                    Context.AssemblyWideTranspile,
                    Context.CurrentAssembly
                )
            )
            .ToList();

        if (nested.Count == 0)
            return;

        var members = new List<TsTopLevel>();
        foreach (var nestedType in nested)
        {
            // Use BuildTypeStatements directly so the nested type's declarations are
            // emitted without going through the file-grouping pipeline. Imports and
            // path computation are the parent file's responsibility (the parent's
            // ImportCollector already walks these statements).
            BuildTypeStatements(nestedType, members);
        }

        if (members.Count > 0)
        {
            statements.Add(
                new TsNamespaceDeclaration(
                    Context.ResolveTsName(parent),
                    Functions: [],
                    Members: members
                )
            );
        }
    }

    /// <summary>
    /// Discovers all types with [Transpile] and transforms each into a TsSourceFile.
    /// Generates namespace-based folder structure and index.ts barrel files.
    /// </summary>
    public IReadOnlyList<TsSourceFile> TransformAll()
    {
        _currentAssembly = compilation.Assembly;

        // The frontend already detects [assembly: TranspileAssembly] (semantic model
        // first, syntax-tree fallback for inline test compilations) — read it off the
        // IR rather than redoing the same probe.
        _assemblyWideTranspile = ir.AssemblyWideTranspile;

        // Copy the frontend-produced map so any in-transformer
        // rewrites (e.g., StripInterfacePrefix) stay scoped to this
        // run without mutating the shared IR.
        var typeNamesBySymbol = ir.TypeNamesBySymbol is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(ir.TypeNamesBySymbol, StringComparer.Ordinal);

        // Frontend owns discovery now — read the ordered entry list off the
        // IR and project it back to raw symbols for the existing per-type
        // emission helpers (GroupTypesByFile, BuildTypeStatements). The
        // synthetic-Program flag + the separate EntryPoint record carry the
        // routing metadata that used to live on the target-side
        // `_syntheticEntryPoint` field.
        var transpilableTypeEntries =
            ir.TranspilableTypeEntries ?? Array.Empty<IrTranspilableTypeEntry>();
        var transpilableTypes = transpilableTypeEntries.Select(e => e.Symbol).ToList();

        // Clone the frontend-produced TranspilableTypes map so the
        // strip pass (and any future in-transformer renamer) can
        // rewrite entries without mutating the shared IR.
        var transpilableTypesDict = ir.TranspilableTypes is null
            ? new Dictionary<string, IrTranspilableTypeRef>(StringComparer.Ordinal)
            : new Dictionary<string, IrTranspilableTypeRef>(
                ir.TranspilableTypes,
                StringComparer.Ordinal
            );

        // Opt-in `I`-prefix strip runs once per transform, mutating
        // both name dictionaries so every downstream consumer
        // (ResolveTsName, file-name derivation, the import collector
        // that walks IrTranspilableTypeRef) sees the same rewritten
        // identifier. The rename dictionary also feeds
        // IrToTsTypeMapper.NamedTypeRenames so emitted type
        // references at usage sites (method parameters, property
        // types, generic arguments) pick up the stripped identifier
        // at the emit boundary.
        var nameRenames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (StripInterfacePrefix)
            ApplyInterfacePrefixStrip(
                transpilableTypes,
                typeNamesBySymbol,
                transpilableTypesDict,
                nameRenames,
                _diagnostics
            );

        // Build the explicit per-compilation context that replaces TypeMapper statics.
        // Omit the optional sinks so the context provisions concurrent defaults
        // (ConcurrentHashSet for misses, ConcurrentDictionary for used packages) —
        // the per-group transform loop below fans out across worker threads.
        var typeMappingContext = new TypeMappingContext(
            ir.BclExports,
            ir.CrossAssemblyOrigins,
            ir.AssembliesNeedingEmitPackage
        );

        // All callers now use the explicit TypeMappingContext — no static assignment needed.

        _pathNaming = new PathNaming(ir.LocalRootNamespace);

        var declarativeMappings = DeclarativeMappingRegistry.FromIr(ir);

        var (erasableFunctionExports, synthesizedAliasesByFile) = BuildNoContainerFunctionExports(
            transpilableTypes,
            transpilableTypesDict,
            ir.BclExports,
            ir.ExternalImports,
            ir.CrossAssemblyOrigins,
            compilation,
            AddDiagnostic
        );

        _context = new TypeScriptTransformContext(
            compilation,
            _currentAssembly,
            _assemblyWideTranspile,
            transpilableTypesDict,
            ir.ExternalImports,
            ir.BclExports,
            typeNamesBySymbol,
            ir.GuardableTypeKeys ?? new HashSet<string>(StringComparer.Ordinal),
            _pathNaming,
            declarativeMappings,
            AddDiagnostic
        )
        {
            TypeMapping = typeMappingContext,
            UseIrBodiesWhenCovered = UseIrBodiesWhenCovered,
            NoContainerFunctionExports = erasableFunctionExports,
            SynthesizedAliasesByFile = synthesizedAliasesByFile,
        };

        var files = new List<TsSourceFile>();
        var groups = new List<TypeFileGroup>();
        var perGroupClosure = new Dictionary<string, string>(StringComparer.Ordinal);
        var perGroupMetadata = Array.Empty<CachedFileMetadata[]>();

        // Publish the interface-prefix rename dict for the duration
        // of this transform so `IrToTsTypeMapper.MapNamed` rewrites
        // every TsNamedType identifier emitted below. The try/finally
        // guarantees the static slot is cleared even when a bridge
        // throws, keeping subsequent TransformAll runs clean.
        var previousRenames = IrToTsTypeMapper.NamedTypeRenames;
        var previousDelegatePredicate = IrTypeRefMapper.NamedDelegatePredicate;
        try
        {
            IrToTsTypeMapper.NamedTypeRenames = nameRenames.Count > 0 ? nameRenames : null;
            IrTypeRefMapper.NamedDelegatePredicate = sym =>
                SymbolHelper.IsTranspilable(sym, _assemblyWideTranspile, _currentAssembly)
                || Context.OriginResolver?.Invoke(sym) is not null;

            // Group types by output file. Types decorated with
            // [EmitInFile("name")] share the same file; everything else gets
            // its own file (legacy 1:1 default). The grouping is keyed by
            // (namespace, fileName) so types with the same EmitInFile value
            // but different namespaces don't accidentally collide — that case
            // is rejected later as MS0008.
            //
            // Each group transforms independently: TransformGroup reads the
            // shared TypeScriptTransformContext (immutable after the setup
            // above), publishes its own per-group AsyncLocal slots for
            // UsingAliases, and only writes back into thread-safe sinks
            // (TypeMapping.CrossPackageMisses is a ConcurrentHashSet,
            // UsedCrossPackages is ConcurrentDictionary-backed, and
            // _diagnostics is guarded by a lock). Parallel.For captures the
            // ambient ExecutionContext when each iteration starts so
            // AsyncLocal writes inside a worker stay isolated. The result
            // list preserves source order via the group index so downstream
            // barrels and golden tests stay deterministic.
            groups = GroupTypesByFile(transpilableTypes);

            // Per-group skip decision (PR 3c): for every group whose
            // closure hash matches the cached entry, reuse the stub
            // built from cached metadata and skip TransformGroup
            // entirely. Misses run the full transform as before. The
            // closure-hash index amortises per-type hashing across
            // groups whose closures overlap.
            perGroupClosure = ComputePerGroupClosure(ir, groups);
            var cachedGroups = LoadGroupCache();
            var perGroupResults = new TsSourceFile?[groups.Count];
            perGroupMetadata = new CachedFileMetadata[groups.Count][];
            var skippedGroupCount = 0;
            Parallel.For(
                0,
                groups.Count,
                index =>
                {
                    var group = groups[index];
                    var groupKey = GroupKey(group);
                    if (
                        cachedGroups is not null
                        && perGroupClosure.TryGetValue(groupKey, out var closure)
                        && cachedGroups.Groups.TryGetValue(groupKey, out var entry)
                        && string.Equals(entry.ClosureHash, closure, StringComparison.Ordinal)
                    )
                    {
                        // Hit: rebuild a stub TsSourceFile from cached
                        // metadata. The host has already verified each
                        // output file exists with matching content via
                        // PR 3a's OutputsStillValid gate; if we got
                        // here it means PR 3a missed for some other
                        // reason but this group's slice is still good.
                        if (entry.Files.Count == 1 && CacheOutputDir is not null)
                        {
                            var fileMeta = entry.Files[0];
                            var diskPath = Path.Combine(
                                CacheOutputDir,
                                fileMeta.Path.Replace('/', Path.DirectorySeparatorChar)
                            );
                            if (File.Exists(diskPath))
                            {
                                perGroupResults[index] = FileMetadataExtractor.BuildStub(fileMeta);
                                perGroupMetadata[index] = [fileMeta];
                                var diskContent = File.ReadAllText(diskPath);
                                var prefixBlock = string.IsNullOrEmpty(CacheFilePrefix)
                                    ? null
                                    : CacheFilePrefix + "\n";
                                if (
                                    prefixBlock is not null
                                    && diskContent.StartsWith(prefixBlock, StringComparison.Ordinal)
                                )
                                {
                                    diskContent = diskContent[prefixBlock.Length..];
                                }
                                _cachedFileContents[fileMeta.Path] = diskContent;
                                Interlocked.Increment(ref skippedGroupCount);
                                return;
                            }
                        }
                    }

                    var produced = TransformGroup(group);
                    perGroupResults[index] = produced;
                    perGroupMetadata[index] = produced is null
                        ? []
                        : [FileMetadataExtractor.Extract(produced)];
                }
            );
            for (var i = 0; i < perGroupResults.Length; i++)
            {
                if (perGroupResults[i] is { } file)
                    files.Add(file);
            }
            if (skippedGroupCount > 0)
            {
                Console.WriteLine(
                    $"  Per-group cache: {skippedGroupCount}/{groups.Count} group(s) reused from cache."
                );
            }
        }
        finally
        {
            IrToTsTypeMapper.NamedTypeRenames = previousRenames;
            IrTypeRefMapper.NamedDelegatePredicate = previousDelegatePredicate;
        }

        // Generate index.ts barrel files per namespace folder
        var indexFiles = BarrelFileGenerator.Generate(files, NamespaceBarrels);
        files.AddRange(indexFiles);

        // Detect cyclic #/ imports between the generated files and emit MS0005
        // diagnostics for each distinct cycle. Cycles are reported as warnings — the
        // build proceeds, but the consumer sees the chain in their build log instead
        // of debugging it through tsgo's downstream error.
        CyclicReferenceDetector.DetectAndReport(files, _diagnostics.Add);

        // Drain MS0007 cross-package misses recorded by TypeMapper.ResolveOrigin while
        // mapping types. One error per unique miss; the message names the missing
        // attribute and the producing assembly so the user knows where to fix it.
        foreach (
            var miss in typeMappingContext.CrossPackageMisses.OrderBy(
                s => s,
                StringComparer.Ordinal
            )
        )
        {
            _diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.CrossPackageResolution,
                    $"Cannot resolve cross-package import for type '{miss}': its containing "
                        + $"assembly declares [TranspileAssembly] but no [EmitPackage] for the "
                        + $"JavaScript target. Add [assembly: EmitPackage(\"<package-name>\")] to "
                        + $"the producing project so consumers can import this type."
                )
            );
        }

        // Drain auto-generated cross-package dependencies. The map is already
        // pre-formatted (string → version specifier), populated by three paths in
        // TypeMapper / ImportCollector. The CLI driver merges it into the consumer's
        // package.json.
        foreach (var (packageName, version) in typeMappingContext.UsedCrossPackages)
        {
            _crossPackageDependencies[packageName] = version;
        }

        WriteGroupCache(groups, perGroupClosure, perGroupMetadata);

        return files;
    }

    private bool _assemblyWideTranspile;
    private IAssemblySymbol? _currentAssembly;
    private PathNaming _pathNaming = new("");

    /// <summary>
    /// Built once after the setup phase of <see cref="TransformAll"/> completes.
    /// All per-type transformation code reads its shared state through this context
    /// instead of touching the private fields directly. Access via <see cref="Context"/>
    /// to fail fast if a per-type helper is invoked before <see cref="TransformAll"/>.
    /// </summary>
    private TypeScriptTransformContext? _context;

    /// <summary>Non-nullable view of <see cref="_context"/> — every per-type helper goes
    /// through this property so a misuse (helper called before <see cref="TransformAll"/>
    /// finishes its setup phase) raises a clear <see cref="InvalidOperationException"/>
    /// instead of a generic <see cref="NullReferenceException"/>.</summary>
    private TypeScriptTransformContext Context =>
        _context
        ?? throw new InvalidOperationException(
            "TypeScriptTransformContext is not yet initialized — TransformAll() must "
                + "complete its setup phase before any per-type helper runs."
        );

    /// <summary>
    /// Builds the top-level statements for a single type into <paramref name="sink"/>,
    /// without computing the file path or collecting imports. Returns true if the type
    /// produced any statements (and is therefore part of a file group); false if it's
    /// a no-op (e.g., <c>[Import]</c> or <c>[Ignore]</c>).
    /// </summary>
    private bool BuildTypeStatements(
        INamedTypeSymbol type,
        List<TsTopLevel> sink,
        IDictionary<INamedTypeSymbol, IrTypeDeclaration>? irCache = null
    )
    {
        // [Import] types are external — don't generate .ts files
        if (SymbolHelper.HasImport(type))
            return false;

        // [Ignore] types are ambient/declaration-only — discoverable in C# so consumers
        // can reference them in signatures, but no .ts file is generated and no import
        // is emitted. Used for structural shapes over external library types.
        if (SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript))
            return false;

        var startCount = sink.Count;

        if (type.TypeKind == TypeKind.Enum)
        {
            var enumIr = (IrEnumDeclaration)GetOrExtractIr(type, irCache)!;
            IrToTsEnumBridge.Convert(enumIr, sink);
        }
        else if (type.TypeKind == TypeKind.Interface)
        {
            var ifaceIr = (IrInterfaceDeclaration)GetOrExtractIr(type, irCache)!;
            IrToTsInterfaceBridge.Convert(ifaceIr, sink, Context.ResolveTsName(type));
        }
        else if (type.TypeKind == TypeKind.Delegate)
        {
            var delegateIr = (IrDelegateDeclaration)GetOrExtractIr(type, irCache)!;
            IrToTsDelegateBridge.Convert(delegateIr, sink, Context.ResolveTsName(type));
        }
        else if (IsExceptionType(type))
        {
            var exceptionIr = (IrClassDeclaration)GetOrExtractIr(type, irCache)!;
            IrToTsExceptionBridge.Convert(exceptionIr, sink, Context.DeclarativeMappings);
        }
        else if (IsJsonSerializerContextType(type))
        {
            new JsonSerializerContextTransformer(Context).Transform(type, sink);
        }
        else if (
            ir.EntryPoint is not null
            && SymbolEqualityComparer.Default.Equals(type, ir.EntryPoint.ContainingType)
        )
        {
            // C# 9+ top-level statements → unwrap as module-level code
            EmitTopLevelStatements(ir.EntryPoint.Method, sink);
        }
        else if (
            (
                SymbolHelper.HasExportedAsModule(type)
                || SymbolHelper.HasNoContainer(type)
                || HasExtensionMembers(type)
            ) && type.IsStatic
        )
        {
            TryEmitModuleViaIr(type, sink);
        }
        else if (TryEmitBrandedViaIr(type, sink, irCache)) { }
        else if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
        {
            if (TryEmitPlainObjectViaIr(type, sink, irCache))
            {
                // Fully emitted through the IR pipeline.
            }
            else
            {
                new IrToTsClassEmitter(Context).Transform(type, sink);
            }
        }

        if (sink.Count == startCount)
            return false;

        // Generate type guard functions when [GenerateGuard] is present.
        // The builder returns both isT (narrowing predicate) and assertT
        // (throwing variant) so a consumer can pick either path depending
        // on whether missed narrowing should be an exception (trust
        // boundary) or a branch (conditional handling).
        if (SymbolHelper.HasGenerateGuard(type))
        {
            foreach (var guard in new TypeGuardBuilder(Context).Generate(type))
                sink.Add(guard);
        }

        // Process nested types — emit a companion namespace with the nested members.
        // TypeScript declaration merging makes `Outer.Inner` accessible just like in C#.
        TransformNestedTypes(type, sink);

        return true;
    }

    /// <summary>
    /// Transforms a group of types that share an output file. Each type's statements
    /// are concatenated in the order the types were discovered, then a single
    /// <see cref="ImportCollector"/> pass collects imports for the merged file. The
    /// resulting <see cref="TsSourceFile"/>'s namespace is taken from the first type
    /// in the group (all types in a valid group share a namespace; conflicts are
    /// flagged as MS0008 by <see cref="GroupTypesByFile"/>).
    /// </summary>
    private TsSourceFile? TransformGroup(TypeFileGroup group)
    {
        // Per-group IR cache: each type is extracted at most once, then shared by the
        // bridge converters (BuildTypeStatements) and the runtime-requirement scanner
        // below. Without this, plain-object classes were extracted three times per group.
        var irCache = new Dictionary<INamedTypeSymbol, IrTypeDeclaration>(
            SymbolEqualityComparer.Default
        );
        var statements = new List<TsTopLevel>();
        var anyEmitted = false;

        // Per-file C# `using X = Y;` aliases substitute canonical type
        // names with the user-declared alias for the duration of this
        // group's emission. Restoring the previous slot in finally keeps
        // the AsyncLocal state clean across groups (and across parallel
        // test runs).
        var primarySyntaxTree = group
            .Types[0]
            .DeclaringSyntaxReferences.FirstOrDefault()
            ?.SyntaxTree;
        var previousAliases = IrToTsTypeMapper.UsingAliases;
        IrToTsTypeMapper.UsingAliases = MergeAliasesForGroup(primarySyntaxTree);

        try
        {
            foreach (var type in group.Types)
            {
                if (BuildTypeStatements(type, statements, irCache))
                    anyEmitted = true;
            }

            if (!anyEmitted)
                return null;

            // Import collection runs inside the same alias scope: body-side
            // identifiers like `new ColumnWidget(...)` carry the alias text
            // only and need the AliasToCanonical map to recover the
            // imported canonical name.
            var primaryType = group.Types[0];
            var irRequirements = ScanIrRuntimeRequirements(group.Types, irCache);
            var imports = new ImportCollector(Context, irRequirements).Collect(
                primaryType,
                statements
            );
            statements.InsertRange(0, imports);

            var relativePath = Context.PathNaming.GetRelativePath(group.Namespace, group.FileName);
            return new TsSourceFile(relativePath, statements, group.Namespace);
        }
        finally
        {
            IrToTsTypeMapper.UsingAliases = previousAliases;
        }
    }

    /// <summary>
    /// Merges three alias sources in precedence order (lowest first, so
    /// later writes overwrite):
    /// <list type="number">
    ///   <item>Auto-synthesized fallbacks (Stage 2) — the implicit safety
    ///   net when a factory shadows a transpilable type.</item>
    ///   <item>User-declared <c>using X = Y;</c> aliases (Layer A).</item>
    ///   <item><c>[ImportAlias]</c> entries on a <c>file class</c>
    ///   carrier (Layer B) — most explicit, wins everything.</item>
    /// </list>
    /// </summary>
    private UsingAliasScope? MergeAliasesForGroup(SyntaxTree? primarySyntaxTree)
    {
        var layerA = UsingAliasResolver.ResolveForTree(primarySyntaxTree, Context.Compilation);
        var filePath = primarySyntaxTree?.FilePath ?? string.Empty;
        Context.SynthesizedAliasesByFile.TryGetValue(filePath, out var synthesized);
        Context.ImportAliasOverrides.TryGetValue(filePath, out var layerB);
        return ImportAliasResolver.Merge(layerA, synthesized, layerB);
    }

    /// <summary>
    /// Builds the union of <see cref="IrRuntimeRequirement"/> facts for every type in
    /// the file group. The IR scanner only needs the type's declared shape (no bodies,
    /// no compilation context), so we run it for every supported kind regardless of
    /// which emitter handled the actual TS lowering. Types that don't go through any
    /// IR extractor today (synthetic top-level entry points, [Import]/[Ignore] types)
    /// are simply skipped — the legacy walker still picks up their template-level
    /// runtime needs.
    /// </summary>
    private IReadOnlySet<IrRuntimeRequirement> ScanIrRuntimeRequirements(
        IReadOnlyList<INamedTypeSymbol> types,
        IDictionary<INamedTypeSymbol, IrTypeDeclaration> irCache
    )
    {
        var acc = new HashSet<IrRuntimeRequirement>();
        foreach (var type in types)
        {
            if (
                SymbolHelper.HasImport(type)
                || SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript)
            )
                continue;
            // Synthetic top-level entry points are wrapped in a class but emitted
            // as module-level code by EmitTopLevelStatements; the IR class
            // extraction would produce an irrelevant shape, so skip it.
            if (
                ir.EntryPoint is not null
                && SymbolEqualityComparer.Default.Equals(type, ir.EntryPoint.ContainingType)
            )
                continue;

            var typeIr = GetOrExtractIr(type, irCache);
            if (typeIr is null)
                continue;

            foreach (var req in IrRuntimeRequirementScanner.Scan(typeIr))
                acc.Add(req);
        }
        return acc;
    }

    /// <summary>
    /// Groups discovered types into file buckets. Types decorated with
    /// <c>[EmitInFile("name")]</c> share a bucket keyed by <c>(namespace, name)</c>;
    /// everything else falls back to its own bucket keyed by <c>(namespace, kebab-case-of-type-name)</c>.
    /// Types in the same group preserve discovery order so the file's declarations are
    /// emitted in the same order as the source assembly walked them.
    /// </summary>
    private List<TypeFileGroup> GroupTypesByFile(IReadOnlyList<INamedTypeSymbol> types)
    {
        // Use an OrderedDictionary-like structure: a list of groups + a lookup for
        // existing keys. This preserves insertion order so the output is deterministic.
        var groups = new List<TypeFileGroup>();
        var byKey = new Dictionary<(string Namespace, string FileName), TypeFileGroup>();

        // Track the namespace conflict case: same file name appears in two namespaces.
        var seenFileNames = new Dictionary<string, string>();

        foreach (var type in types)
        {
            var ns = PathNaming.GetNamespace(type);
            var explicitFile = SymbolHelper.GetEmitInFile(type);
            var fileName =
                explicitFile is not null && explicitFile.Length > 0
                    ? SymbolHelper.ToKebabCase(explicitFile)
                    : SymbolHelper.ToKebabCase(Context.ResolveTsName(type));

            // MS0008: when a type opts into [EmitInFile], the file name must be unique
            // per namespace. If we've seen the same file name in a different namespace,
            // that's an ambiguous folder placement.
            if (
                explicitFile is not null
                && seenFileNames.TryGetValue(fileName, out var firstNs)
                && firstNs != ns
            )
            {
                _diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.EmitInFileConflict,
                        $"[EmitInFile(\"{explicitFile}\")] on type '{type.ToDisplayString()}' "
                            + $"conflicts with another type that uses the same file name in namespace "
                            + $"'{firstNs}'. Co-located types must share a namespace.",
                        type.Locations.FirstOrDefault()
                    )
                );
                continue;
            }
            seenFileNames.TryAdd(fileName, ns);

            var key = (ns, fileName);
            if (!byKey.TryGetValue(key, out var group))
            {
                group = new TypeFileGroup(ns, fileName, new List<INamedTypeSymbol>());
                byKey[key] = group;
                groups.Add(group);
            }
            group.Types.Add(type);
        }

        return groups;
    }

    private sealed record TypeFileGroup(
        string Namespace,
        string FileName,
        List<INamedTypeSymbol> Types
    );

    private static string GroupKey(TypeFileGroup group) => $"{group.Namespace}/{group.FileName}";

    /// <summary>
    /// Builds the per-group closure hash map (PR 3c). The signature
    /// hash index is amortised: a type appearing in N groups gets
    /// hashed once.
    /// </summary>
    private static Dictionary<string, string> ComputePerGroupClosure(
        IrCompilation ir,
        IReadOnlyList<TypeFileGroup> groups
    )
    {
        var allTypes = groups
            .SelectMany(g => g.Types)
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>();
        var sigIndex = GroupClosureHasher.BuildSignatureHashIndex(allTypes);
        var graph = IrTypeDependencyGraph.Build(ir);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var fqns = group.Types.Select(IrTypeDependencyGraph.QualifiedName);
            result[GroupKey(group)] = GroupClosureHasher.HashGroupClosure(fqns, graph, sigIndex);
        }
        return result;
    }

    private GroupCacheFile? LoadGroupCache() =>
        CacheOutputDir is null ? null : GroupCacheFile.TryRead(CacheOutputDir);

    private void WriteGroupCache(
        IReadOnlyList<TypeFileGroup> groups,
        IReadOnlyDictionary<string, string> closures,
        IReadOnlyList<CachedFileMetadata[]> metadata
    )
    {
        if (CacheOutputDir is null)
            return;
        var entries = new Dictionary<string, GroupCacheEntry>(StringComparer.Ordinal);
        for (var i = 0; i < groups.Count; i++)
        {
            var key = GroupKey(groups[i]);
            if (!closures.TryGetValue(key, out var closure))
                continue;
            var files = metadata[i] ?? [];
            if (files.Length == 0)
                continue;
            entries[key] = new GroupCacheEntry(closure, files);
        }
        var cache = new GroupCacheFile(GroupCacheFile.CurrentFormatVersion, entries);
        cache.Write(CacheOutputDir);
    }

    /// <summary>
    /// Rewrites every top-level transpilable interface whose C# name
    /// matches <c>^I[A-Z]</c> in both
    /// <paramref name="typeNamesBySymbol"/> (read by
    /// <see cref="TypeScriptTransformContext.ResolveTsName"/> and the
    /// interface-bridge <c>nameOverride</c> plumbing) and
    /// <paramref name="transpilableTypesDict"/> (read by
    /// <see cref="ImportCollector"/> + guard builder). An interface
    /// whose stripped name would collide with another top-level type
    /// in the same enclosing scope (namespace for top-level types,
    /// containing-type for nested ones) keeps the prefix and
    /// surfaces <c>MS0017</c> so the consumer notices and picks an
    /// explicit rename. Interfaces that already carry a
    /// <c>[Name(TypeScript, "…")]</c> override skip the pass — the
    /// author-chosen name wins. Nested interfaces are deliberately
    /// out of scope for this first slice; they retain their original
    /// names because the downstream lowering has not yet been
    /// audited for a rename there.
    /// </summary>
    private static void ApplyInterfacePrefixStrip(
        IReadOnlyList<INamedTypeSymbol> transpilableTypes,
        Dictionary<string, string> typeNamesBySymbol,
        Dictionary<string, IrTranspilableTypeRef> transpilableTypesDict,
        Dictionary<string, string> nameRenames,
        List<MetanoDiagnostic> diagnostics
    )
    {
        // Index the sibling space once so collision checks stay O(1).
        // The scope key is the type's *enclosing container*: the
        // containing type (`…+Outer`) when the interface is nested,
        // or the namespace display string for top-level types. Using
        // `ToDisplayString()` on the namespace would falsely collide
        // nested interfaces across different outer types that happen
        // to share a namespace.
        var scopeIndex = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var t in transpilableTypes)
        {
            var scope = GetEnclosingScopeKey(t);
            var key = t.GetCrossAssemblyOriginKey();
            if (!typeNamesBySymbol.TryGetValue(key, out var emitted))
                emitted = t.Name;
            if (!scopeIndex.TryGetValue(scope, out var bucket))
            {
                bucket = new HashSet<string>(StringComparer.Ordinal);
                scopeIndex[scope] = bucket;
            }
            bucket.Add(emitted);
        }

        foreach (var type in transpilableTypes)
        {
            if (type.TypeKind != TypeKind.Interface)
                continue;
            // Nested interfaces skip the rewrite in this slice — the
            // bridge + file-naming paths for nested types have not
            // been audited for the renamed identifier, and a mixed
            // shape (stripped top-level but prefixed nested) would
            // surprise consumers. Follow-up work can lift this.
            if (type.ContainingType is not null)
                continue;
            var key = type.GetCrossAssemblyOriginKey();
            if (!typeNamesBySymbol.TryGetValue(key, out var current))
                continue;
            // `[Name(TypeScript, "…")]` wins. If the frontend-stored
            // name differs from the Roslyn name, an override is
            // already in play — leave it alone.
            if (!string.Equals(current, type.Name, StringComparison.Ordinal))
                continue;
            if (!LooksLikeInterfacePrefix(current))
                continue;

            var stripped = current[1..];
            var scope = GetEnclosingScopeKey(type);
            var siblings = scopeIndex[scope];
            if (siblings.Contains(stripped))
            {
                var ns = type.ContainingNamespace is { IsGlobalNamespace: false } nsSymbol
                    ? nsSymbol.ToDisplayString()
                    : null;
                var location = ns is null ? "the global namespace" : $"namespace '{ns}'";
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Warning,
                        DiagnosticCodes.InterfacePrefixCollision,
                        $"Cannot strip the 'I' prefix from '{current}' in {location} — "
                            + $"another top-level type named '{stripped}' already lives in "
                            + $"the same scope. Keeping the prefixed name. Rename the "
                            + $"conflicting type or set [Name(TypeScript, \"…\")] to resolve.",
                        type.Locations.FirstOrDefault()
                    )
                );
                continue;
            }

            typeNamesBySymbol[key] = stripped;
            siblings.Remove(current);
            siblings.Add(stripped);
            RewriteTranspilableTypeEntry(transpilableTypesDict, current, stripped);
            nameRenames[current] = stripped;
        }
    }

    /// <summary>
    /// Propagates the rewritten name into
    /// <see cref="TypeScriptTransformContext.TranspilableTypes"/>.
    /// The frontend registers each entry under its source name (and
    /// its TS alias when they diverge). Under the strip, the source
    /// name (<c>IIssueRepository</c>) goes away from the dict and
    /// the stripped form (<c>IssueRepository</c>) becomes the
    /// consumer-visible key, with <c>TsName</c> + <c>FileName</c>
    /// updated so the import collector emits the rewritten file
    /// path and identifier.
    /// </summary>
    private static void RewriteTranspilableTypeEntry(
        Dictionary<string, IrTranspilableTypeRef> dict,
        string sourceName,
        string stripped
    )
    {
        if (!dict.TryGetValue(sourceName, out var entry))
            return;
        var rewrittenFileName = string.Equals(entry.FileName, SymbolHelper.ToKebabCase(sourceName))
            ? SymbolHelper.ToKebabCase(stripped)
            : entry.FileName;
        var updated = entry with { TsName = stripped, FileName = rewrittenFileName };
        dict.Remove(sourceName);
        dict[stripped] = updated;
    }

    private static string GetEnclosingScopeKey(INamedTypeSymbol type) =>
        type.ContainingType is { } outer
            ? outer.ToDisplayString()
            : (
                type.ContainingNamespace is { IsGlobalNamespace: false } ns
                    ? ns.ToDisplayString()
                    : string.Empty
            );

    /// <summary>
    /// Matches <c>^I[A-Z]</c> — captures <c>IFoo</c> and
    /// <c>ISO8601</c> alike. <c>[Name(TypeScript, "…")]</c> is the
    /// documented escape hatch for names users explicitly want to
    /// keep (<c>IOrder</c> that is not an interface, acronym-led
    /// names).
    /// </summary>
    private static bool LooksLikeInterfacePrefix(string name) =>
        name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]);

    /// <summary>
    /// Lowers an <c>[ExportedAsModule]</c> static class (or any static class
    /// holding extension methods / extension blocks) through
    /// <see cref="IrToTsModuleBridge"/>. The body of a single
    /// <c>[ModuleEntryPoint]</c> is unwrapped as top-level module statements
    /// after the regular functions. Returns <c>true</c> when the type was
    /// handled (even if only diagnostics were emitted) and <c>false</c> only
    /// when the body coverage probe rejects it — at which point the caller
    /// produces no output for the type.
    /// </summary>
    private bool TryEmitModuleViaIr(INamedTypeSymbol type, List<TsTopLevel> sink)
    {
        if (!Context.UseIrBodiesWhenCovered)
            return false;
        // Walk members once to find the entry point AND surface the
        // multiple-[ModuleEntryPoint] diagnostic.
        IMethodSymbol? entryPoint = null;
        var diagnosed = false;
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol m && SymbolHelper.HasModuleEntryPoint(m))
            {
                if (entryPoint is not null)
                {
                    ReportInvalidEntryPoint(
                        m,
                        $"Type '{type.Name}' declares multiple [ModuleEntryPoint] "
                            + "methods. Only one is allowed per class."
                    );
                    diagnosed = true;
                    continue;
                }
                entryPoint = m;
            }
        }

        var functions = IrModuleFunctionExtractor
            .Extract(type, Context.OriginResolver, Context.Compilation, TargetLanguage.TypeScript)
            // IrModuleFunctionExtractor emits every public method. Strip the
            // entry point — its body is unwrapped separately below.
            .Where(f =>
                entryPoint is null
                || !string.Equals(f.Name, entryPoint.Name, StringComparison.Ordinal)
            )
            .ToList();

        // Validate + extract entry point body. Invalid signatures surface
        // diagnostics here and the entry point is dropped; ordinary module
        // functions still emit so the rest of the file is salvageable.
        IReadOnlyList<IrStatement>? entryBody = null;
        if (entryPoint is not null)
        {
            if (entryPoint.Parameters.Length > 0)
            {
                ReportInvalidEntryPoint(
                    entryPoint,
                    $"[ModuleEntryPoint] method '{entryPoint.Name}' must have no parameters."
                );
                entryPoint = null;
                diagnosed = true;
            }
            else if (!IsValidEntryPointReturn(entryPoint.ReturnType))
            {
                ReportInvalidEntryPoint(
                    entryPoint,
                    $"[ModuleEntryPoint] method '{entryPoint.Name}' must return void, Task, "
                        + $"or ValueTask. Found: {entryPoint.ReturnType.ToDisplayString()}."
                );
                entryPoint = null;
                diagnosed = true;
            }
            else
            {
                entryBody = ExtractMethodBody(entryPoint);
                if (entryBody is null || !IrBodyCoverageProbe.IsFullyCovered(entryBody))
                {
                    ReportUnsupportedBody(
                        entryPoint,
                        $"[ModuleEntryPoint] body of '{type.Name}.{entryPoint.Name}' contains "
                            + "constructs the IR pipeline doesn't yet model; the type was skipped."
                    );
                    return true;
                }
            }
        }

        // No work to do: when a diagnostic was already raised, swallow the
        // type so the caller's `sink.Count == startCount` check skips file
        // emission without falling back to a now-deleted legacy path.
        if (functions.Count == 0 && entryBody is null)
            return diagnosed;
        foreach (var fn in functions)
        {
            if (fn.Body is null || !IrBodyCoverageProbe.IsFullyCovered(fn.Body))
            {
                ReportUnsupportedBody(
                    type,
                    $"Module function '{type.Name}.{fn.Name}' contains constructs the IR "
                        + "pipeline doesn't yet model; the type was skipped."
                );
                return true;
            }
        }

        IrToTsModuleBridge.Convert(functions, sink, Context.DeclarativeMappings);

        // Entry point body flows as top-level module statements after the
        // ordinary functions, so it can reference them.
        if (entryBody is not null && entryPoint is not null)
        {
            var exportInfo = SymbolHelper.GetExportVarFromBody(entryPoint);
            if (exportInfo is { AsDefault: true, InPlace: true })
            {
                ReportInvalidEntryPoint(
                    entryPoint,
                    $"[ExportVarFromBody(\"{exportInfo.Name}\")] on "
                        + $"'{entryPoint.Name}' cannot combine AsDefault = true with "
                        + $"InPlace = true. Default exports must be emitted as a "
                        + $"separate trailing statement; set InPlace = false."
                );
                exportInfo = null;
            }

            var lowered = IrToTsStatementBridge
                .MapBody(entryBody, Context.DeclarativeMappings)
                .ToList();
            TsTopLevel? trailingExport = null;
            var foundLocal = false;
            for (var i = 0; i < lowered.Count; i++)
            {
                if (
                    exportInfo is not null
                    && lowered[i] is TsVariableDeclaration varDecl
                    && varDecl.Name == exportInfo.Name
                )
                {
                    foundLocal = true;
                    if (exportInfo.InPlace)
                        lowered[i] = varDecl with { Exported = true };
                    else
                        trailingExport = new TsModuleExport(exportInfo.Name, exportInfo.AsDefault);
                }
                sink.Add(new TsTopLevelStatement(lowered[i]));
            }

            if (exportInfo is not null && !foundLocal)
            {
                ReportInvalidEntryPoint(
                    entryPoint,
                    $"[ExportVarFromBody(\"{exportInfo.Name}\")] on "
                        + $"'{entryPoint.Name}' did not find a local variable named "
                        + $"'{exportInfo.Name}' in the entry point body."
                );
            }

            if (trailingExport is not null)
                sink.Add(trailingExport);
        }
        return true;
    }

    private void ReportInvalidEntryPoint(IMethodSymbol method, string message) =>
        Context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.InvalidModuleEntryPoint,
                message,
                method.Locations.FirstOrDefault()
            )
        );

    private void ReportUnsupportedBody(ISymbol contextSymbol, string message) =>
        Context.ReportUnsupportedBody(contextSymbol, message);

    /// <summary>
    /// Lowers C# 9+ top-level statements into module-level TS statements. The
    /// synthetic entry point's declaring syntax is the
    /// <see cref="CompilationUnitSyntax"/> that hosts the
    /// <see cref="GlobalStatementSyntax"/> nodes; we walk those directly so
    /// the <c>const x = …</c> promotion + <c>TryGetValue</c> expansion that
    /// <see cref="IrStatementExtractor.ExtractStatements(IReadOnlyList{StatementSyntax})"/>
    /// applies match the body-extraction path.
    /// </summary>
    private void EmitTopLevelStatements(IMethodSymbol syntheticEntryPoint, List<TsTopLevel> sink)
    {
        var syntaxRef = syntheticEntryPoint.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
            return;

        var tree = syntaxRef.SyntaxTree;
        var semanticModel = Context.Compilation.GetSemanticModel(tree);
        var stmtExtractor = new IrStatementExtractor(
            semanticModel,
            target: TargetLanguage.TypeScript
        );

        // GlobalStatementSyntax is always a direct child of CompilationUnitSyntax —
        // walking ChildNodes avoids descending into the bodies of nested types,
        // local functions, or lambdas declared in the file.
        var globalStatements = ((CompilationUnitSyntax)tree.GetRoot())
            .Members.OfType<GlobalStatementSyntax>()
            .Select(g => g.Statement)
            .ToList();

        var irBody = stmtExtractor.ExtractStatements(globalStatements);
        foreach (var ts in IrToTsStatementBridge.MapBody(irBody, Context.DeclarativeMappings))
            sink.Add(new TsTopLevelStatement(ts));
    }

    private IReadOnlyList<IrStatement>? ExtractMethodBody(IMethodSymbol method)
    {
        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (syntax is null || (syntax.Body is null && syntax.ExpressionBody is null))
            return null;
        var model = Context.Compilation.GetSemanticModel(syntax.SyntaxTree);
        return new IrStatementExtractor(model, target: TargetLanguage.TypeScript).ExtractBody(
            syntax.Body,
            syntax.ExpressionBody,
            isVoid: method.ReturnsVoid
        );
    }

    private static bool IsValidEntryPointReturn(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
            return true;
        var original = returnType.OriginalDefinition.ToDisplayString();
        return original is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask";
    }

    private bool TryEmitPlainObjectViaIr(
        INamedTypeSymbol type,
        List<TsTopLevel> sink,
        IDictionary<INamedTypeSymbol, IrTypeDeclaration>? irCache
    )
    {
        if (!SymbolHelper.HasPlainObject(type))
            return false;

        var ir = (IrClassDeclaration)GetOrExtractIr(type, irCache)!;
        return IrToTsPlainObjectBridge.Convert(ir, sink, Context.DeclarativeMappings);
    }

    /// <summary>
    /// Routes <c>[Branded]</c> structs through
    /// <see cref="IrToTsBrandedBridge"/>. The IR class extractor already
    /// records <see cref="IrTypeSemantics.IsBranded"/> +
    /// <see cref="IrTypeSemantics.BrandedUnderlyingType"/>, so the bridge has
    /// every piece it needs to emit the brand alias + companion namespace.
    /// </summary>
    private bool TryEmitBrandedViaIr(
        INamedTypeSymbol type,
        List<TsTopLevel> sink,
        IDictionary<INamedTypeSymbol, IrTypeDeclaration>? irCache
    )
    {
        if (!SymbolHelper.HasBranded(type) || type.TypeKind != TypeKind.Struct)
            return false;

        var ir = (IrClassDeclaration)GetOrExtractIr(type, irCache)!;
        return IrToTsBrandedBridge.Convert(ir, sink, Context.DeclarativeMappings);
    }

    /// <summary>
    /// Returns the IR for <paramref name="type"/>, reusing the per-group cache when
    /// supplied. Returns <c>null</c> for type kinds the IR pipeline doesn't model
    /// (delegates, type parameters). Callers that already gated on a known kind can
    /// safely cast the result.
    /// </summary>
    private IrTypeDeclaration? GetOrExtractIr(
        INamedTypeSymbol type,
        IDictionary<INamedTypeSymbol, IrTypeDeclaration>? irCache
    )
    {
        if (irCache is not null && irCache.TryGetValue(type, out var cached))
            return cached;
        IrTypeDeclaration? ir = type.TypeKind switch
        {
            TypeKind.Enum => IrEnumExtractor.Extract(type),
            TypeKind.Interface => IrInterfaceExtractor.Extract(
                type,
                Context.OriginResolver,
                TargetLanguage.TypeScript
            ),
            TypeKind.Class or TypeKind.Struct => IrClassExtractor.Extract(
                type,
                Context.OriginResolver,
                Context.Compilation,
                TargetLanguage.TypeScript
            ),
            TypeKind.Delegate => IrDelegateExtractor.Extract(
                type,
                Context.OriginResolver,
                TargetLanguage.TypeScript
            ),
            _ => null,
        };
        if (ir is not null && irCache is not null)
            irCache[type] = ir;
        return ir;
    }

    internal static bool IsExceptionType(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    internal static bool IsJsonSerializerContextType(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Text.Json.Serialization.JsonSerializerContext")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Checks if a static class contains extension methods (classic or C# 14 blocks).
    /// </summary>
    internal static bool HasExtensionMembers(INamedTypeSymbol type)
    {
        // Classic extensions
        if (
            type.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(m => m.IsExtensionMethod && m.MethodKind == MethodKind.Ordinary)
        )
            return true;

        // C# 14 extension blocks — detected via syntax
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (
                syntax
                    .DescendantNodes()
                    .Any(n => n.Kind().ToString() == "ExtensionBlockDeclaration")
            )
                return true;
        }

        return false;
    }

    internal static TsAccessibility MapAccessibility(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Private => TsAccessibility.Private,
            Accessibility.Protected or Accessibility.ProtectedOrInternal =>
                TsAccessibility.Protected,
            _ => TsAccessibility.Public,
        };

    /// <summary>
    /// Builds the camelCase-name → declaring-type lookup the import collector
    /// uses to resolve cross-module references to <c>[NoContainer]</c> static
    /// methods. Without it the collector — which keys imports off
    /// <see cref="IrTranspilableTypeRef.TsName"/> — cannot bridge a flattened
    /// call site (<c>column(args)</c>) back to the file the function was
    /// emitted into. Honors <c>[Name]</c> overrides through the same TS
    /// naming policy <see cref="IrToTsModuleBridge"/> uses on the emit side
    /// so both halves agree on the function identifier.
    /// </summary>
    private static (
        IReadOnlyDictionary<string, NoContainerExport> Exports,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SynthesizedAliasesByFile
    ) BuildNoContainerFunctionExports(
        IReadOnlyList<INamedTypeSymbol> transpilableTypes,
        IReadOnlyDictionary<string, IrTranspilableTypeRef> transpilableTypesDict,
        IReadOnlyDictionary<string, IrBclExport> bclExports,
        IReadOnlyDictionary<string, IrExternalImport> externalImports,
        IReadOnlyDictionary<string, IrTypeOrigin> crossAssemblyOrigins,
        Compilation compilation,
        Action<MetanoDiagnostic> reportDiagnostic
    )
    {
        var reservedNames = BuildReservedImportNames(
            transpilableTypes,
            transpilableTypesDict,
            bclExports,
            externalImports
        );

        var exports = new Dictionary<string, NoContainerExport>(StringComparer.Ordinal);
        var claimedFactoryNames = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        var synthesizedAliasesByFile = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.Ordinal
        );

        foreach (var type in transpilableTypes)
        {
            if (!SymbolHelper.HasNoContainer(type))
                continue;
            if (!transpilableTypesDict.TryGetValue(type.Name, out var typeRef))
                continue;

            foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (!IsExportableNoContainerMethod(member))
                    continue;

                var fnName = ResolveNoContainerFunctionName(member);

                if (
                    TryResolveImportNameCollision(
                        member,
                        fnName,
                        reservedNames,
                        compilation,
                        synthesizedAliasesByFile,
                        reportDiagnostic
                    )
                )
                    continue;
                if (
                    TryReportFactoryCollision(member, fnName, claimedFactoryNames, reportDiagnostic)
                )
                    continue;

                exports.Add(fnName, new NoContainerExport(typeRef));
                claimedFactoryNames.Add(fnName, member);
            }
        }

        // Cross-assembly [NoContainer] static classes shipped from a
        // referenced [TranspileAssembly] + [EmitPackage] library. The
        // consumer's flatten lowers their static-method calls to bare
        // identifiers (`UI.Foo(x)` → `foo(x)`), but the type itself is
        // not in `transpilableTypesDict` (own-assembly only). Without
        // this scan, the consumer file emits the call but never imports
        // the function — `tsc` errors. The per-assembly filter mirrors
        // `CSharpSourceFrontend.EnumerateTranspilableReferencedAssemblies`.
        // (#178)
        var emitTargetValue = (int)TargetLanguage.TypeScript.ToEmitTarget();
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                continue;
            if (SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly))
                continue;

            var hasTranspileAssembly = asm.GetAttributes()
                .Any(a =>
                    a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
                );
            if (!hasTranspileAssembly)
                continue;

            var packageInfo = SymbolHelper.GetEmitPackageInfo(asm, emitTargetValue);
            if (packageInfo is null)
                continue;

            CollectTopLevelNoContainerExports(
                asm.GlobalNamespace,
                packageInfo,
                crossAssemblyOrigins,
                exports
            );
        }

        return (
            exports,
            synthesizedAliasesByFile.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, string>)kv.Value,
                StringComparer.Ordinal
            )
        );
    }

    private static void CollectTopLevelNoContainerExports(
        INamespaceSymbol namespaceSymbol,
        SymbolHelper.EmitPackageInfo packageInfo,
        IReadOnlyDictionary<string, IrTypeOrigin> crossAssemblyOrigins,
        Dictionary<string, NoContainerExport> exports
    )
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
            VisitCrossAssemblyType(type, packageInfo, crossAssemblyOrigins, exports);

        foreach (var nestedNs in namespaceSymbol.GetNamespaceMembers())
            CollectTopLevelNoContainerExports(nestedNs, packageInfo, crossAssemblyOrigins, exports);
    }

    private static void VisitCrossAssemblyType(
        INamedTypeSymbol type,
        SymbolHelper.EmitPackageInfo packageInfo,
        IReadOnlyDictionary<string, IrTypeOrigin> crossAssemblyOrigins,
        Dictionary<string, NoContainerExport> exports
    )
    {
        if (type.DeclaredAccessibility == Accessibility.Public && SymbolHelper.HasNoContainer(type))
        {
            var originKey = type.GetCrossAssemblyOriginKey();
            if (crossAssemblyOrigins.TryGetValue(originKey, out var typeOrigin))
            {
                var subPath = PathNaming.ComputeSubPath(
                    typeOrigin.AssemblyRootNamespace ?? "",
                    typeOrigin.Namespace ?? "",
                    SymbolHelper.GetNameOverride(type, TargetLanguage.TypeScript) ?? type.Name
                );
                var origin = new TsTypeOrigin(packageInfo.Name, subPath);
                var stub = new IrTranspilableTypeRef(
                    Key: originKey,
                    TsName: SymbolHelper.GetNameOverride(type, TargetLanguage.TypeScript)
                        ?? type.Name,
                    Namespace: typeOrigin.Namespace ?? "",
                    FileName: SymbolHelper.ToKebabCase(
                        SymbolHelper.GetNameOverride(type, TargetLanguage.TypeScript) ?? type.Name
                    ),
                    IsStringEnum: false
                );
                foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!IsExportableNoContainerMethod(member))
                        continue;
                    var fnName = ResolveNoContainerFunctionName(member);
                    // First-wins: own-assembly entries already registered take priority.
                    if (exports.ContainsKey(fnName))
                        continue;
                    exports.Add(fnName, new NoContainerExport(stub, origin));
                }
            }
        }

        foreach (var nested in type.GetTypeMembers())
            VisitCrossAssemblyType(nested, packageInfo, crossAssemblyOrigins, exports);
    }

    /// <summary>
    /// Collects every TS identifier the import collector would already
    /// reserve in the consuming files: emitted transpilable types, BCL
    /// exports (decimal → Decimal), and <c>[Import]</c> externals. A
    /// <c>[NoContainer]</c> factory whose emitted name collides with any of
    /// these would shadow the existing import at the TS surface, breaking
    /// the consumer file in subtle ways the user would only catch at
    /// <c>tsc</c> time. <c>[NoContainer]</c> owners and <c>[Ignore]</c>
    /// types are skipped because they emit no class and contribute no
    /// surface name.
    /// </summary>
    private static IReadOnlyDictionary<string, ReservedImportName> BuildReservedImportNames(
        IReadOnlyList<INamedTypeSymbol> transpilableTypes,
        IReadOnlyDictionary<string, IrTranspilableTypeRef> transpilableTypesDict,
        IReadOnlyDictionary<string, IrBclExport> bclExports,
        IReadOnlyDictionary<string, IrExternalImport> externalImports
    )
    {
        var reserved = new Dictionary<string, ReservedImportName>(StringComparer.Ordinal);

        foreach (var type in transpilableTypes)
        {
            if (SymbolHelper.HasNoContainer(type))
                continue;
            if (SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript))
                continue;
            if (!transpilableTypesDict.TryGetValue(type.Name, out var typeRef))
                continue;
            reserved[typeRef.TsName] = new ReservedImportName(
                ReservedImportKind.TranspilableType,
                type.ToDisplayString(),
                typeRef
            );
        }

        foreach (var entry in bclExports.Values)
        {
            if (entry.ExportedName.Length == 0)
                continue;
            reserved.TryAdd(
                entry.ExportedName,
                new ReservedImportName(
                    ReservedImportKind.BclExport,
                    $"{entry.ExportedName} (from {entry.FromPackage})"
                )
            );
        }

        foreach (var entry in externalImports.Values)
        {
            if (entry.Name.Length == 0)
                continue;
            reserved.TryAdd(
                entry.Name,
                new ReservedImportName(
                    ReservedImportKind.ExternalImport,
                    $"{entry.Name} (from {entry.From})"
                )
            );
        }

        foreach (var helper in WellKnownRuntimeHelpers)
            reserved.TryAdd(
                helper,
                new ReservedImportName(
                    ReservedImportKind.RuntimeHelper,
                    $"{helper} (metano-runtime)"
                )
            );

        return reserved;
    }

    /// <summary>
    /// Names the TS target injects from <c>metano-runtime</c> through the
    /// import collector. A factory name shadowing one of these would
    /// produce a duplicate-binding TS error or — worse — silently
    /// re-route runtime helper references to a user-defined function.
    /// </summary>
    private static readonly IReadOnlySet<string> WellKnownRuntimeHelpers = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "Enumerable",
        "Grouping",
        "HashCode",
        "HashSet",
        "UUID",
        "bindReceiver",
        "delegateAdd",
        "delegateRemove",
    };

    private enum ReservedImportKind
    {
        TranspilableType,
        BclExport,
        ExternalImport,
        RuntimeHelper,
    }

    private readonly record struct ReservedImportName(
        ReservedImportKind Kind,
        string Description,
        IrTranspilableTypeRef? OwnerRef = null
    );

    private static bool IsExportableNoContainerMethod(IMethodSymbol member) =>
        member.IsStatic
        && member.MethodKind == MethodKind.Ordinary
        && member.DeclaredAccessibility == Accessibility.Public;

    private static string ResolveNoContainerFunctionName(IMethodSymbol member) =>
        SymbolHelper.GetNameOverride(member, TargetLanguage.TypeScript)
        ?? TypeScriptNaming.ToCamelCase(member.Name);

    /// <summary>
    /// Returns <c>true</c> when a diagnostic was reported for
    /// <paramref name="factory"/> shadowing an import name the consumer
    /// file already binds, signaling the caller to skip the export.
    /// </summary>
    /// <summary>
    /// Detects a clash between an <c>[NoContainer]</c> factory name and a
    /// reserved import slot. Layered resolution:
    /// <list type="number">
    ///   <item>If the factory's source file already aliases the colliding
    ///   canonical via <c>using X = Y;</c>, the user's choice wins —
    ///   silent pass-through, factory survives.</item>
    ///   <item>If the colliding slot is a transpilable type and we know
    ///   its emit metadata, synthesize a path-derived alias
    ///   (<c>Column</c> from <c>#/mvu/widgets</c> → <c>ColumnFromWidgets</c>),
    ///   register it for the factory's source file, and emit MS0022
    ///   (Info) so the user can pin it.</item>
    ///   <item>Otherwise (BCL export, external <c>[Import]</c>, runtime
    ///   helper, or a transpilable target without enough metadata),
    ///   fall back to the original MS0020 error — there is no obvious
    ///   alias to derive.</item>
    /// </list>
    /// Returns <c>true</c> when the caller should skip exporting the
    /// factory (Layer 3 only); cases 1 and 2 keep the export valid.
    /// </summary>
    private static bool TryResolveImportNameCollision(
        IMethodSymbol factory,
        string factoryName,
        IReadOnlyDictionary<string, ReservedImportName> reservedNames,
        Compilation compilation,
        Dictionary<string, Dictionary<string, string>> synthesizedAliasesByFile,
        Action<MetanoDiagnostic> reportDiagnostic
    )
    {
        if (!reservedNames.TryGetValue(factoryName, out var reserved))
            return false;

        var factoryTree = factory.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
        var factoryFilePath = factoryTree?.FilePath ?? string.Empty;

        if (
            UsingAliasResolver.ResolveForTree(factoryTree, compilation) is { } scope
            && scope.CanonicalToAlias.ContainsKey(factoryName)
        )
            return false;

        if (reserved.OwnerRef is { } ownerRef)
        {
            var alias = SuggestAliasFromOwnerRef(factoryName, ownerRef);
            if (!synthesizedAliasesByFile.TryGetValue(factoryFilePath, out var fileAliases))
            {
                fileAliases = new Dictionary<string, string>(StringComparer.Ordinal);
                synthesizedAliasesByFile[factoryFilePath] = fileAliases;
            }
            fileAliases[factoryName] = alias;

            var owner = factory.ContainingType!.Name;
            reportDiagnostic(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Info,
                    DiagnosticCodes.AliasedImportConflict,
                    $"[NoContainer] static method '{owner}.{factory.Name}' resolves to TS factory "
                        + $"name '{factoryName}', which collides with transpilable type "
                        + $"'{reserved.Description}'. Imported as '{alias}' inside this file. "
                        + $"Pin the alias deterministically with a 'using {alias} = …;' "
                        + "directive to silence this notice.",
                    factory.Locations.FirstOrDefault()
                )
            );
            return false;
        }

        var ownerName = factory.ContainingType!.Name;
        reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.NoContainerFactoryNameClash,
                $"[NoContainer] static method '{ownerName}.{factory.Name}' resolves to TS factory "
                    + $"name '{factoryName}', which collides with {DescribeReservedKind(reserved.Kind)} "
                    + $"'{reserved.Description}'. Drop the [Name] override or pick a name that "
                    + "does not match an emitted type or imported symbol.",
                factory.Locations.FirstOrDefault()
            )
        );
        return true;
    }

    private static string SuggestAliasFromOwnerRef(string canonical, IrTranspilableTypeRef ownerRef)
    {
        var lastSegment =
            ownerRef.Namespace.LastIndexOf('.') is var dot && dot >= 0
                ? ownerRef.Namespace[(dot + 1)..]
                : ownerRef.Namespace;
        if (string.IsNullOrEmpty(lastSegment))
            return canonical + "Imported";
        return canonical + "From" + Capitalize(lastSegment);
    }

    private static string Capitalize(string value) =>
        value.Length switch
        {
            0 => value,
            1 => value.ToUpperInvariant(),
            _ => char.ToUpperInvariant(value[0]) + value[1..],
        };

    /// <summary>
    /// Anchored on the second occurrence — same convention as MS0008
    /// (<c>EmitInFileConflict</c>). Returns <c>true</c> when a diagnostic
    /// was reported, signaling the caller to skip the export.
    /// </summary>
    private static bool TryReportFactoryCollision(
        IMethodSymbol factory,
        string factoryName,
        IReadOnlyDictionary<string, IMethodSymbol> claimedFactoryNames,
        Action<MetanoDiagnostic> reportDiagnostic
    )
    {
        if (!claimedFactoryNames.TryGetValue(factoryName, out var priorFactory))
            return false;
        var priorOwner = priorFactory.ContainingType!.Name;
        var newOwner = factory.ContainingType!.Name;
        reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.NoContainerFactoryNameClash,
                $"[NoContainer] static method '{newOwner}.{factory.Name}' resolves to TS factory "
                    + $"name '{factoryName}', which is already exported by "
                    + $"'{priorOwner}.{priorFactory.Name}'. Rename one via [Name(\"...\")].",
                factory.Locations.FirstOrDefault()
            )
        );
        return true;
    }

    private static string DescribeReservedKind(ReservedImportKind kind) =>
        kind switch
        {
            ReservedImportKind.TranspilableType => "transpilable type",
            ReservedImportKind.BclExport => "BCL export",
            ReservedImportKind.ExternalImport => "[Import] external",
            ReservedImportKind.RuntimeHelper => "metano-runtime helper",
            _ => "imported symbol",
        };
}
