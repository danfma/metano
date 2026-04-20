using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Transformation;

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
    /// Forwarded to <see cref="TypeScriptTransformContext.UseIrBodiesWhenCovered"/>.
    /// Always <c>true</c> in production — the IR pipeline is the only path for
    /// method-body lowering now that the legacy transformers are gone. Kept as
    /// an init-only kill switch so a regression test can intentionally bypass
    /// the IR bridges and confirm the type-emission code skips the type
    /// without crashing.
    /// </summary>
    public bool UseIrBodiesWhenCovered { get; init; } = true;

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
                new TsNamespaceDeclaration(GetTsTypeName(parent), Functions: [], Members: members)
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

        var transpilableTypes = DiscoverTranspilableTypes();
        // Map by both C# name and TS name (when [Name] override differs)
        _transpilableTypeMap = new Dictionary<string, INamedTypeSymbol>();
        foreach (var t in transpilableTypes)
        {
            _transpilableTypeMap[t.Name] = t;
            var tsName = GetTsTypeName(t);
            if (tsName != t.Name)
                _transpilableTypeMap[tsName] = t;
        }

        // Seed the external-import map from the frontend. The IR covers every
        // [Import] type from the current assembly, plus those from referenced
        // assemblies that opt into transpilation ([TranspileAssembly]) and
        // declare an [EmitPackage] for the active target — keyed by C# simple
        // name, with MS0003 collisions surfaced through the host's diagnostic
        // merge. The TS-name aliasing below is a target concern that stays
        // here until the frontend gains target awareness — it must walk every
        // public top-level [Import] type (mirroring the frontend's walker),
        // not just `transpilableTypes`, so an [Import] placeholder without
        // [Transpile] in a project that does not declare
        // [assembly: TranspileAssembly] still gets its TS-name alias.
        _externalImportMap = new Dictionary<string, IrExternalImport>(
            ir.ExternalImports,
            StringComparer.Ordinal
        );
        RegisterTsNameAliases();

        // Build the explicit per-compilation context that replaces TypeMapper statics.
        var crossPackageMisses = new HashSet<string>();
        var usedCrossPackages = new Dictionary<string, string>();
        var typeMappingContext = new TypeMappingContext(
            ir.BclExports,
            ir.CrossAssemblyOrigins,
            ir.AssembliesNeedingEmitPackage,
            crossPackageMisses,
            usedCrossPackages
        );

        // All callers now use the explicit TypeMappingContext — no static assignment needed.

        // Build guard name → type name map for cross-file guard imports
        _guardNameToTypeMap = new Dictionary<string, string>();
        foreach (var t in transpilableTypes)
        {
            var tsName = GetTsTypeName(t);
            _guardNameToTypeMap[$"is{tsName}"] = tsName;
        }

        _pathNaming = new PathNaming(ir.LocalRootNamespace);

        var declarativeMappings = DeclarativeMappingRegistry.FromIr(ir);

        _context = new TypeScriptTransformContext(
            compilation,
            _currentAssembly,
            _assemblyWideTranspile,
            _transpilableTypeMap,
            _externalImportMap,
            ir.BclExports,
            _guardNameToTypeMap,
            _pathNaming,
            declarativeMappings,
            _diagnostics.Add
        )
        {
            TypeMapping = typeMappingContext,
            UseIrBodiesWhenCovered = UseIrBodiesWhenCovered,
        };

        var files = new List<TsSourceFile>();

        // Group types by output file. Types decorated with [EmitInFile("name")] share
        // the same file; everything else gets its own file (legacy 1:1 default). The
        // grouping is keyed by (namespace, fileName) so types with the same EmitInFile
        // value but different namespaces don't accidentally collide — that case is
        // rejected later as MS0008.
        foreach (var group in GroupTypesByFile(transpilableTypes))
        {
            var file = TransformGroup(group);
            if (file is not null)
                files.Add(file);
        }

        // Generate index.ts barrel files per namespace folder
        var indexFiles = BarrelFileGenerator.Generate(files);
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

        return files;
    }

    private bool _assemblyWideTranspile;
    private IAssemblySymbol? _currentAssembly;

    /// <summary>
    /// The compiler-synthesized entry point method from C# 9+ top-level statements.
    /// When set, the containing type is routed through
    /// <see cref="EmitTopLevelStatements"/> as an implicit
    /// <c>[ExportedAsModule]</c> + <c>[ModuleEntryPoint]</c>.
    /// </summary>
    private IMethodSymbol? _syntheticEntryPoint;
    private Dictionary<string, INamedTypeSymbol> _transpilableTypeMap = [];
    private Dictionary<string, IrExternalImport> _externalImportMap = [];

    /// <summary>
    /// Maps guard function names (e.g., "isCurrency") to the type they guard (e.g., "Currency").
    /// Used to resolve imports for cross-file guard calls.
    /// </summary>
    private Dictionary<string, string> _guardNameToTypeMap = [];
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
    /// Registers the <c>[Name(TypeScript)]</c> alias into
    /// <see cref="_externalImportMap"/> for every <c>[Import]</c>-annotated
    /// type whose TS name differs from the C# source name. Walks the
    /// current assembly plus every referenced assembly that opts into
    /// transpilation via <c>[TranspileAssembly]</c> and declares an
    /// <c>[EmitPackage]</c> for the active target.
    /// <para>
    /// The C#-source-name keys are already produced by the frontend via
    /// <see cref="IrCompilation.ExternalImports"/>; only TS-name aliasing
    /// stays on the target side, pending a target-aware frontend.
    /// </para>
    /// </summary>
    private void RegisterTsNameAliases()
    {
        RegisterTsNameAliasesForAssembly(compilation.Assembly);

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

            if (
                SymbolHelper.GetEmitPackageInfo(asm, targetEnumValue: (int)EmitTarget.JavaScript)
                is null
            )
                continue;

            RegisterTsNameAliasesForAssembly(asm);
        }
    }

    private void RegisterTsNameAliasesForAssembly(IAssemblySymbol assembly)
    {
        foreach (var type in EnumeratePublicTopLevelTypes(assembly.GlobalNamespace))
        {
            var import = SymbolHelper.GetImport(type);
            if (import is null)
                continue;

            var tsName = GetTsTypeName(type);
            if (tsName == type.Name)
                continue;

            var entry = new IrExternalImport(
                Name: import.Name,
                From: import.From,
                IsDefault: import.AsDefault,
                Version: import.Version
            );
            RegisterExternalImportMapping(
                tsName,
                entry,
                type.ToDisplayString(),
                type.Locations.FirstOrDefault()
            );
        }
    }

    /// <summary>
    /// Yields every public top-level type reachable from
    /// <paramref name="ns"/>. Mirrors the frontend's walker so the TS-name
    /// aliasing pass can register every <c>[Import]</c> type the IR
    /// already covers, including placeholders that the user did not mark
    /// with <c>[Transpile]</c>. No <c>[NoTranspile]</c>/<c>[NoEmit]</c>
    /// filter is applied because an <c>[Import]</c> is an external binding
    /// — emission gating is irrelevant for its TS-name alias.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> EnumeratePublicTopLevelTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    foreach (var nested in EnumeratePublicTopLevelTypes(childNs))
                        yield return nested;
                    break;
                case INamedTypeSymbol type
                    when type.ContainingType is null
                        && type.DeclaredAccessibility == Accessibility.Public:
                    yield return type;
                    break;
            }
        }
    }

    private IReadOnlyList<INamedTypeSymbol> DiscoverTranspilableTypes()
    {
        var types = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(typeDecl);
                if (symbol is not INamedTypeSymbol namedType)
                    continue;
                // Skip nested types — they're processed by their containing type as companion namespaces
                if (namedType.ContainingType is not null)
                    continue;
                if (
                    !SymbolHelper.IsTranspilable(
                        namedType,
                        _assemblyWideTranspile,
                        _currentAssembly
                    )
                )
                    continue;
                if (!seen.Add(namedType))
                    continue;
                types.Add(namedType);
            }
        }

        // Detect C# 9+ top-level statements. Only fires when the syntax tree
        // actually contains GlobalStatementSyntax — an explicit `static void Main()`
        // in a Program class (traditional entry point) must NOT be routed through
        // TransformTopLevelStatements, because there are no global statements to walk.
        if (_assemblyWideTranspile && compilation is CSharpCompilation csharpComp)
        {
            var hasGlobalStatements = compilation.SyntaxTrees.Any(t =>
                t.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any()
            );

            if (hasGlobalStatements)
            {
                var entryPoint = csharpComp.GetEntryPoint(System.Threading.CancellationToken.None);
                if (
                    entryPoint is not null
                    && entryPoint.ContainingType is { } programType
                    && !SymbolHelper.HasExportedAsModule(programType)
                    && seen.Add(programType)
                )
                {
                    types.Add(programType);
                    _syntheticEntryPoint = entryPoint;
                }
            }
        }

        return types;
    }

    /// <summary>
    /// Builds the top-level statements for a single type into <paramref name="sink"/>,
    /// without computing the file path or collecting imports. Returns true if the type
    /// produced any statements (and is therefore part of a file group); false if it's
    /// a no-op (e.g., <c>[Import]</c> or <c>[NoEmit]</c>).
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

        // [NoEmit] types are ambient/declaration-only — discoverable in C# so consumers
        // can reference them in signatures, but no .ts file is generated and no import
        // is emitted. Used for structural shapes over external library types.
        if (SymbolHelper.HasNoEmit(type, TargetLanguage.TypeScript))
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
            IrToTsInterfaceBridge.Convert(ifaceIr, sink);
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
            _syntheticEntryPoint is not null
            && SymbolEqualityComparer.Default.Equals(type, _syntheticEntryPoint.ContainingType)
        )
        {
            // C# 9+ top-level statements → unwrap as module-level code
            EmitTopLevelStatements(_syntheticEntryPoint, sink);
        }
        else if (
            (SymbolHelper.HasExportedAsModule(type) || HasExtensionMembers(type)) && type.IsStatic
        )
        {
            TryEmitModuleViaIr(type, sink);
        }
        else if (TryEmitInlineWrapperViaIr(type, sink, irCache)) { }
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

        // Generate type guard function when [GenerateGuard] is present
        if (SymbolHelper.HasGenerateGuard(type))
        {
            var guard = new TypeGuardBuilder(Context).Generate(type);
            if (guard is not null)
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
        foreach (var type in group.Types)
        {
            if (BuildTypeStatements(type, statements, irCache))
                anyEmitted = true;
        }

        if (!anyEmitted)
            return null;

        // The import collector takes a "current type" so it can elide self-imports for
        // a type's own guard function. We pass the first type in the group; for
        // multi-type files, the elision still works for the primary type, and other
        // types in the same file aren't imported anyway (they're locally declared).
        var primaryType = group.Types[0];
        var irRequirements = ScanIrRuntimeRequirements(group.Types, irCache);
        var imports = new ImportCollector(
            Context.TranspilableTypeMap,
            Context.ExternalImportMap,
            Context.BclExportMap,
            Context.GuardNameToTypeMap,
            Context.PathNaming,
            Context.TypeMapping!,
            irRequirements
        ).Collect(primaryType, statements);
        statements.InsertRange(0, imports);

        var relativePath = Context.PathNaming.GetRelativePath(group.Namespace, group.FileName);
        return new TsSourceFile(relativePath, statements, group.Namespace);
    }

    /// <summary>
    /// Builds the union of <see cref="IrRuntimeRequirement"/> facts for every type in
    /// the file group. The IR scanner only needs the type's declared shape (no bodies,
    /// no compilation context), so we run it for every supported kind regardless of
    /// which emitter handled the actual TS lowering. Types that don't go through any
    /// IR extractor today (synthetic top-level entry points, [Import]/[NoEmit] types)
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
                || SymbolHelper.HasNoEmit(type, TargetLanguage.TypeScript)
            )
                continue;
            // Synthetic top-level entry points are wrapped in a class but emitted
            // as module-level code by EmitTopLevelStatements; the IR class
            // extraction would produce an irrelevant shape, so skip it.
            if (
                _syntheticEntryPoint is not null
                && SymbolEqualityComparer.Default.Equals(type, _syntheticEntryPoint.ContainingType)
            )
                continue;

            var ir = GetOrExtractIr(type, irCache);
            if (ir is null)
                continue;

            foreach (var req in IrRuntimeRequirementScanner.Scan(ir))
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
                    : SymbolHelper.ToKebabCase(GetTsTypeName(type));

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

    private void RegisterExternalImportMapping(
        string key,
        IrExternalImport entry,
        string ownerDisplayName,
        Location? location
    )
    {
        if (!_externalImportMap.TryGetValue(key, out var existing))
        {
            _externalImportMap[key] = entry;
            return;
        }

        if (existing == entry)
            return;

        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousConstruct,
                $"External import name collision for '{key}'. Keeping '{existing.From}' "
                    + $"('{existing.Name}') and ignoring conflicting mapping from "
                    + $"'{ownerDisplayName}' to '{entry.From}' ('{entry.Name}').",
                location
            )
        );
    }

    /// <summary>
    /// Returns the TypeScript name for a type. Uses [Name] override if present, otherwise the C# name as-is.
    /// </summary>
    internal static string GetTsTypeName(INamedTypeSymbol type)
    {
        return SymbolHelper.GetNameOverride(type, TargetLanguage.TypeScript) ?? type.Name;
    }

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
    /// Routes <c>[InlineWrapper]</c> structs through
    /// <see cref="IrToTsInlineWrapperBridge"/>. The IR class extractor already
    /// records <see cref="IrTypeSemantics.IsInlineWrapper"/> +
    /// <see cref="IrTypeSemantics.InlineWrappedType"/>, so the bridge has
    /// every piece it needs to emit the brand alias + companion namespace.
    /// </summary>
    private bool TryEmitInlineWrapperViaIr(
        INamedTypeSymbol type,
        List<TsTopLevel> sink,
        IDictionary<INamedTypeSymbol, IrTypeDeclaration>? irCache
    )
    {
        if (!SymbolHelper.HasInlineWrapper(type) || type.TypeKind != TypeKind.Struct)
            return false;

        var ir = (IrClassDeclaration)GetOrExtractIr(type, irCache)!;
        return IrToTsInlineWrapperBridge.Convert(ir, sink, Context.DeclarativeMappings);
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
                target: TargetLanguage.TypeScript
            ),
            TypeKind.Class or TypeKind.Struct => IrClassExtractor.Extract(
                type,
                Context.OriginResolver,
                Context.Compilation,
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
}
