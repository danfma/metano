using MetaSharp.Compiler;
using MetaSharp.Compiler.Diagnostics;
using MetaSharp.TypeScript;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Transforms C# types annotated with [Transpile] into TypeScript AST source files.
/// </summary>
public sealed class TypeTransformer(Compilation compilation)
{
    private readonly List<MetaSharpDiagnostic> _diagnostics = [];

    /// <summary>
    /// Diagnostics collected during transformation. Includes warnings about unsupported
    /// language features and other issues that the user should know about.
    /// </summary>
    public IReadOnlyList<MetaSharpDiagnostic> Diagnostics => _diagnostics;

    private readonly Dictionary<string, string> _crossPackageDependencies = new();

    /// <summary>
    /// Maps each cross-package npm name that was actually referenced during
    /// transformation to its npm version specifier (<c>^Major.Minor.Patch</c>, or
    /// <c>workspace:*</c> when the source assembly has no explicit version). Drained
    /// from <see cref="TypeMapper.UsedCrossPackages"/> at the end of <c>TransformAll</c>
    /// and surfaced to the CLI driver so the package.json writer can merge the entries
    /// into <c>dependencies</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> CrossPackageDependencies => _crossPackageDependencies;

    internal void ReportDiagnostic(MetaSharpDiagnostic diagnostic)
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
        var nested = parent.GetTypeMembers()
            .Where(t => !t.IsImplicitlyDeclared)
            .Where(t => t.DeclaredAccessibility != Accessibility.Internal)
            .Where(t => SymbolHelper.IsTranspilable(t, _context!.AssemblyWideTranspile, _context.CurrentAssembly))
            .ToList();

        if (nested.Count == 0) return;

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
            statements.Add(new TsNamespaceDeclaration(
                GetTsTypeName(parent),
                Functions: [],
                Members: members));
        }
    }

    /// <summary>
    /// Discovers all types with [Transpile] and transforms each into a TsSourceFile.
    /// Generates namespace-based folder structure and index.ts barrel files.
    /// </summary>
    public IReadOnlyList<TsSourceFile> TransformAll()
    {
        _currentAssembly = compilation.Assembly;

        // Read [ExportFromBcl] assembly-level attributes
        LoadBclExportMappings();

        // Detect [assembly: TranspileAssembly] — MUST happen before DiscoverTranspilableTypes
        // Check both semantic model (for real projects) and syntax tree (for inline compilation)
        _assemblyWideTranspile = compilation.Assembly.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly")
            || compilation.SyntaxTrees.Any(tree => tree.GetRoot()
                .DescendantNodes().OfType<AttributeListSyntax>()
                .Any(al => al.Target?.Identifier.Text == "assembly"
                    && al.Attributes.Any(a =>
                    {
                        var name = a.Name.ToString();
                        return name is "TranspileAssembly" or "TranspileAssemblyAttribute"
                            or "MetaSharp.TranspileAssembly" or "MetaSharp.TranspileAssemblyAttribute";
                    })));

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

        // Register [Import] types as external (no .ts file generated, but importable)
        _externalImportMap = new Dictionary<string, (string Name, string From, bool IsDefault, string? Version)>();
        foreach (var t in transpilableTypes.ToList())
        {
            var import = SymbolHelper.GetImport(t);
            if (import is not null)
            {
                var entry = (import.Name, import.From, import.AsDefault, import.Version);
                _externalImportMap[t.Name] = entry;
                var tsName = GetTsTypeName(t);
                if (tsName != t.Name)
                    _externalImportMap[tsName] = entry;
            }
        }

        // Discover transpilable types from referenced assemblies (those that declare
        // both [TranspileAssembly] and [EmitPackage(JavaScript)]). Populates
        // _crossAssemblyTypeMap and augments _externalImportMap with [Import] entries
        // from the referenced assemblies. Must run after the local _externalImportMap
        // is built so it only adds, never overwrites local entries.
        DiscoverCrossAssemblyTypes();
        TypeMapper.CrossAssemblyTypeMap = _crossAssemblyTypeMap;
        TypeMapper.AssembliesNeedingEmitPackage = _assembliesNeedingEmitPackage;
        TypeMapper.CrossPackageMisses = new HashSet<string>();
        TypeMapper.UsedCrossPackages = new Dictionary<string, string>();

        // Build guard name → type name map for cross-file guard imports
        _guardNameToTypeMap = new Dictionary<string, string>();
        foreach (var t in transpilableTypes)
        {
            var tsName = GetTsTypeName(t);
            _guardNameToTypeMap[$"is{tsName}"] = tsName;
        }

        // Detect root namespace (longest common prefix)
        var namespaces = transpilableTypes
            .Select(PathNaming.GetNamespace)
            .Where(ns => ns.Length > 0)
            .ToList();

        _pathNaming = new PathNaming(
            namespaces.Count > 0 ? PathNaming.FindCommonNamespacePrefix(namespaces) : "");

        // Read declarative [MapMethod]/[MapProperty] from the current assembly + every
        // referenced assembly. The registry is consulted by BclMapper before its hardcoded
        // lowering rules.
        var declarativeMappings = DeclarativeMappingRegistry.BuildFromCompilation(compilation);

        _context = new TypeScriptTransformContext(
            compilation,
            _currentAssembly,
            _assemblyWideTranspile,
            _transpilableTypeMap,
            _externalImportMap,
            _bclExportMap,
            _guardNameToTypeMap,
            _pathNaming,
            declarativeMappings,
            _diagnostics.Add);

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
        foreach (var miss in TypeMapper.CrossPackageMisses.OrderBy(s => s, StringComparer.Ordinal))
        {
            _diagnostics.Add(new MetaSharpDiagnostic(
                MetaSharpDiagnosticSeverity.Error,
                DiagnosticCodes.CrossPackageResolution,
                $"Cannot resolve cross-package import for type '{miss}': its containing " +
                $"assembly declares [TranspileAssembly] but no [EmitPackage] for the " +
                $"JavaScript target. Add [assembly: EmitPackage(\"<package-name>\")] to " +
                $"the producing project so consumers can import this type."));
        }

        // Drain auto-generated cross-package dependencies. The map is already
        // pre-formatted (string → version specifier), populated by three paths in
        // TypeMapper / ImportCollector. The CLI driver merges it into the consumer's
        // package.json.
        foreach (var (packageName, version) in TypeMapper.UsedCrossPackages)
        {
            _crossPackageDependencies[packageName] = version;
        }

        return files;
    }

    private bool _assemblyWideTranspile;
    private IAssemblySymbol? _currentAssembly;
    private Dictionary<string, INamedTypeSymbol> _transpilableTypeMap = [];
    private Dictionary<string, (string Name, string From, bool IsDefault, string? Version)> _externalImportMap = [];
    private Dictionary<string, (string ExportedName, string FromPackage, string Version)> _bclExportMap = [];
    /// <summary>
    /// Types discovered in referenced assemblies that declare both
    /// <c>[TranspileAssembly]</c> and <c>[EmitPackage(JavaScript)]</c>. Keyed by symbol
    /// identity (not name) to handle the case where two assemblies expose types with
    /// the same simple name. Consumed by <see cref="TypeMapper"/> when computing the
    /// origin of a referenced type, and by the import collector to emit cross-package
    /// import statements.
    /// </summary>
    private Dictionary<ISymbol, CrossAssemblyEntry> _crossAssemblyTypeMap =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Referenced assemblies that have <c>[TranspileAssembly]</c> but lack
    /// <c>[EmitPackage(JavaScript)]</c>. The type mapper consults this set when a
    /// cross-assembly type lookup misses; if the type's containing assembly is here,
    /// it raises MS0007 in <see cref="TypeMapper.CrossPackageMisses"/> so the
    /// transformer can report the missing-attribute error at the consumer site.
    /// </summary>
    private HashSet<IAssemblySymbol> _assembliesNeedingEmitPackage =
        new(SymbolEqualityComparer.Default);
    /// <summary>
    /// Maps guard function names (e.g., "isCurrency") to the type they guard (e.g., "Currency").
    /// Used to resolve imports for cross-file guard calls.
    /// </summary>
    private Dictionary<string, string> _guardNameToTypeMap = [];
    private PathNaming _pathNaming = new("");

    /// <summary>
    /// Built once after the setup phase of <see cref="TransformAll"/> completes.
    /// All per-type transformation code reads its shared state through this context
    /// instead of touching the private fields directly.
    /// </summary>
    private TypeScriptTransformContext? _context;

    /// <summary>
    /// Walks <see cref="Compilation.References"/> and, for each referenced assembly that
    /// declares <em>both</em> <c>[TranspileAssembly]</c> and <c>[EmitPackage(JavaScript)]</c>,
    /// enumerates its public transpilable types and registers them in
    /// <see cref="_crossAssemblyTypeMap"/>. Also augments <see cref="_externalImportMap"/>
    /// with any <c>[Import]</c> declarations from those assemblies so consumers can
    /// transitively reach external bindings declared in a referenced library.
    ///
    /// Each cross-assembly type is paired with the root namespace of <em>its</em> source
    /// assembly so the subpath inside the package can be computed independently of the
    /// consumer's own root namespace.
    /// </summary>
    private void DiscoverCrossAssemblyTypes()
    {
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                continue;
            // Skip the assembly currently being compiled.
            if (SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly)) continue;

            // Cheap filter: only consider assemblies that opt into transpilation.
            var hasTranspileAssembly = asm.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly");
            if (!hasTranspileAssembly) continue;

            // Must also declare an EmitPackage for the JavaScript target — without it,
            // we have no package name to import from. The assembly is still a candidate
            // (it's transpilable in principle), so we register it in
            // _assembliesNeedingEmitPackage so the type mapper can detect references to
            // its types and report MS0007 at the consumer site instead of failing
            // silently with a downstream TypeScript "undefined identifier" error.
            var packageInfo = SymbolHelper.GetEmitPackageInfo(asm, targetEnumValue: 0);
            if (packageInfo is null)
            {
                _assembliesNeedingEmitPackage.Add(asm);
                continue;
            }
            var packageName = packageInfo.Name;
            var versionOverride = packageInfo.Version;

            // First pass: enumerate every transpilable type in the assembly so we can
            // compute its root namespace.
            var assemblyTypes = new List<INamedTypeSymbol>();
            CollectTypesFromNamespace(asm.GlobalNamespace, assemblyTypes);

            var namespaces = assemblyTypes
                .Select(PathNaming.GetNamespace)
                .Where(ns => ns.Length > 0)
                .ToList();
            var rootNs = namespaces.Count > 0
                ? PathNaming.FindCommonNamespacePrefix(namespaces)
                : "";

            // Second pass: register each type in the cross-assembly map. Tipos com
            // [Import] continuam acessíveis pro consumer mas vão para o
            // _externalImportMap (pacote externo de origem), não pro cross-assembly
            // map (que assume "vem do package emitido por aquele assembly").
            foreach (var type in assemblyTypes)
            {
                var import = SymbolHelper.GetImport(type);
                if (import is not null)
                {
                    var entry = (import.Name, import.From, import.AsDefault, import.Version);
                    _externalImportMap[type.Name] = entry;
                    var tsName = GetTsTypeName(type);
                    if (tsName != type.Name)
                        _externalImportMap[tsName] = entry;
                    continue;
                }

                _crossAssemblyTypeMap[type] = new CrossAssemblyEntry(type, packageName, rootNs, versionOverride);
            }
        }
    }

    /// <summary>
    /// Recursively walks an <see cref="INamespaceSymbol"/> and collects every public
    /// top-level type whose declaration would be transpiled (passes <see cref="SymbolHelper.IsTranspilable"/>
    /// with assembly-wide opt-in). Nested types are skipped — they're processed by
    /// their containing type as companion namespaces.
    /// </summary>
    private static void CollectTypesFromNamespace(
        INamespaceSymbol ns, List<INamedTypeSymbol> sink)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectTypesFromNamespace(childNs, sink);
                    break;
                case INamedTypeSymbol type
                    when type.ContainingType is null
                         && type.DeclaredAccessibility == Accessibility.Public
                         && !SymbolHelper.HasNoTranspile(type)
                         && !SymbolHelper.HasNoEmit(type):
                    sink.Add(type);
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
                if (symbol is not INamedTypeSymbol namedType) continue;
                // Skip nested types — they're processed by their containing type as companion namespaces
                if (namedType.ContainingType is not null) continue;
                if (!SymbolHelper.IsTranspilable(namedType, _assemblyWideTranspile, _currentAssembly)) continue;
                if (!seen.Add(namedType)) continue;
                types.Add(namedType);
            }
        }

        return types;
    }

    private void LoadBclExportMappings()
    {
        _bclExportMap = new Dictionary<string, (string ExportedName, string FromPackage, string Version)>();

        // Read [ExportFromBcl] from the current assembly first, then from every
        // referenced assembly so that built-in mappings (e.g., decimal → Decimal from
        // decimal.js, declared in MetaSharp/Runtime/Decimal.cs) flow through without
        // the user having to redeclare them in their own project. The current assembly
        // is processed last so user overrides win on conflict.
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm
                && !SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly))
                LoadBclExportFromAssembly(asm);
        }
        LoadBclExportFromAssembly(compilation.Assembly);

        // Make BCL export map available to TypeMapper
        TypeMapper.BclExportMap = _bclExportMap;
    }

    private void LoadBclExportFromAssembly(IAssemblySymbol assembly)
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("ExportFromBclAttribute" or "ExportFromBcl"))
                continue;

            // Constructor arg is typeof(Type)
            if (attr.ConstructorArguments.Length == 0) continue;
            var typeArg = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (typeArg is null) continue;

            var exportedName = "";
            var fromPackage = "";
            var version = "";

            foreach (var namedArg in attr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "ExportedName":
                        exportedName = namedArg.Value.Value?.ToString() ?? "";
                        break;
                    case "FromPackage":
                        fromPackage = namedArg.Value.Value?.ToString() ?? "";
                        break;
                    case "Version":
                        version = namedArg.Value.Value?.ToString() ?? "";
                        break;
                }
            }

            if (exportedName.Length > 0)
            {
                _bclExportMap[typeArg.ToDisplayString()] = (exportedName, fromPackage, version);
            }
        }
    }

    /// <summary>
    /// Builds the top-level statements for a single type into <paramref name="sink"/>,
    /// without computing the file path or collecting imports. Returns true if the type
    /// produced any statements (and is therefore part of a file group); false if it's
    /// a no-op (e.g., <c>[Import]</c> or <c>[NoEmit]</c>).
    /// </summary>
    private bool BuildTypeStatements(INamedTypeSymbol type, List<TsTopLevel> sink)
    {
        // [Import] types are external — don't generate .ts files
        if (SymbolHelper.HasImport(type))
            return false;

        // [NoEmit] types are ambient/declaration-only — discoverable in C# so consumers
        // can reference them in signatures, but no .ts file is generated and no import
        // is emitted. Used for structural shapes over external library types.
        if (SymbolHelper.HasNoEmit(type))
            return false;

        var startCount = sink.Count;

        if (type.TypeKind == TypeKind.Enum)
        {
            EnumTransformer.Transform(type, sink);
        }
        else if (type.TypeKind == TypeKind.Interface)
        {
            InterfaceTransformer.Transform(type, sink);
        }
        else if (IsExceptionType(type))
        {
            new ExceptionTransformer(_context!).Transform(type, sink);
        }
        else if ((SymbolHelper.HasExportedAsModule(type) || HasExtensionMembers(type)) && type.IsStatic)
        {
            new ModuleTransformer(_context!).Transform(type, sink);
        }
        else if (new InlineWrapperTransformer(_context!).Transform(type, sink))
        {
            // InlineWrapper handled by specialized pipeline.
        }
        else if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
        {
            new RecordClassTransformer(_context!).Transform(type, sink);
        }

        if (sink.Count == startCount)
            return false;

        // Generate type guard function when [GenerateGuard] is present
        if (SymbolHelper.HasGenerateGuard(type))
        {
            var guard = new TypeGuardBuilder(_context!.TranspilableTypeMap).Generate(type);
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
        var statements = new List<TsTopLevel>();
        var anyEmitted = false;
        foreach (var type in group.Types)
        {
            if (BuildTypeStatements(type, statements))
                anyEmitted = true;
        }

        if (!anyEmitted) return null;

        // The import collector takes a "current type" so it can elide self-imports for
        // a type's own guard function. We pass the first type in the group; for
        // multi-type files, the elision still works for the primary type, and other
        // types in the same file aren't imported anyway (they're locally declared).
        var primaryType = group.Types[0];
        var imports = new ImportCollector(
            _context!.TranspilableTypeMap,
            _context.ExternalImportMap,
            _context.BclExportMap,
            _context.GuardNameToTypeMap,
            _context.PathNaming)
            .Collect(primaryType, statements);
        statements.InsertRange(0, imports);

        var relativePath = _context!.PathNaming.GetRelativePath(group.Namespace, group.FileName);
        return new TsSourceFile(relativePath, statements, group.Namespace);
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
            var fileName = explicitFile is not null && explicitFile.Length > 0
                ? SymbolHelper.ToKebabCase(explicitFile)
                : SymbolHelper.ToKebabCase(GetTsTypeName(type));

            // MS0008: when a type opts into [EmitInFile], the file name must be unique
            // per namespace. If we've seen the same file name in a different namespace,
            // that's an ambiguous folder placement.
            if (explicitFile is not null && seenFileNames.TryGetValue(fileName, out var firstNs)
                && firstNs != ns)
            {
                _diagnostics.Add(new MetaSharpDiagnostic(
                    MetaSharpDiagnosticSeverity.Error,
                    DiagnosticCodes.EmitInFileConflict,
                    $"[EmitInFile(\"{explicitFile}\")] on type '{type.ToDisplayString()}' " +
                    $"conflicts with another type that uses the same file name in namespace " +
                    $"'{firstNs}'. Co-located types must share a namespace.",
                    type.Locations.FirstOrDefault()));
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

    private sealed record TypeFileGroup(string Namespace, string FileName, List<INamedTypeSymbol> Types);

    /// <summary>
    /// Returns the TypeScript name for a type. Uses [Name] override if present, otherwise the C# name as-is.
    /// </summary>
    internal static string GetTsTypeName(INamedTypeSymbol type)
    {
        return SymbolHelper.GetNameOverride(type) ?? type.Name;
    }

    internal static bool IsExceptionType(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Exception") return true;
            current = current.BaseType;
        }

        return false;
    }

    internal static bool TryGetInlineWrapperPrimitiveType(INamedTypeSymbol type, out TsType primitiveType)
    {
        primitiveType = new TsAnyType();

        var valueMembers = new List<ITypeSymbol>();

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.IsStatic) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (SymbolHelper.HasIgnore(member)) continue;

            switch (member)
            {
                case IPropertySymbol prop when prop.GetMethod is not null && prop.Parameters.Length == 0:
                    valueMembers.Add(prop.Type);
                    break;
                case IFieldSymbol field:
                    valueMembers.Add(field.Type);
                    break;
            }
        }

        if (valueMembers.Count != 1)
            return false;

        var mapped = TypeMapper.Map(valueMembers[0]);
        if (mapped is TsStringType or TsNumberType or TsBooleanType or TsBigIntType)
        {
            primitiveType = mapped;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a static class contains extension methods (classic or C# 14 blocks).
    /// </summary>
    internal static bool HasExtensionMembers(INamedTypeSymbol type)
    {
        // Classic extensions
        if (type.GetMembers().OfType<IMethodSymbol>()
            .Any(m => m.IsExtensionMethod && m.MethodKind == MethodKind.Ordinary))
            return true;

        // C# 14 extension blocks — detected via syntax
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax.DescendantNodes().Any(n => n.Kind().ToString() == "ExtensionBlockDeclaration"))
                return true;
        }

        return false;
    }





    internal static IReadOnlyList<TsTypeParameter>? ExtractTypeParameters(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0) return null;
        return type.TypeParameters.Select(tp =>
        {
            TsType? constraint = null;
            if (tp.ConstraintTypes.Length > 0)
                constraint = TypeMapper.Map(tp.ConstraintTypes[0]);
            return new TsTypeParameter(tp.Name, constraint);
        }).ToList();
    }

    internal static IReadOnlyList<TsTypeParameter>? ExtractMethodTypeParameters(IMethodSymbol method)
    {
        // Method has its own type parameters
        if (method.TypeParameters.Length > 0)
        {
            return method.TypeParameters.Select(tp =>
            {
                TsType? constraint = null;
                if (tp.ConstraintTypes.Length > 0)
                    constraint = TypeMapper.Map(tp.ConstraintTypes[0]);
                return new TsTypeParameter(tp.Name, constraint);
            }).ToList();
        }

        // Static methods in generic classes: promote class type params to method level
        // In TS, static members cannot reference class type parameters
        if (method.IsStatic && method.ContainingType?.TypeParameters.Length > 0)
        {
            var classTypeParams = method.ContainingType.TypeParameters;
            // Check if the method actually uses any class type params
            var usedParams = classTypeParams.Where(tp =>
                ReferencesTypeParam(method.ReturnType, tp)
                || method.Parameters.Any(p => ReferencesTypeParam(p.Type, tp))
            ).ToList();

            if (usedParams.Count > 0)
            {
                return usedParams.Select(tp =>
                {
                    TsType? constraint = null;
                    if (tp.ConstraintTypes.Length > 0)
                        constraint = TypeMapper.Map(tp.ConstraintTypes[0]);
                    return new TsTypeParameter(tp.Name, constraint);
                }).ToList();
            }
        }

        return null;
    }


    private static bool ReferencesTypeParam(ITypeSymbol type, ITypeParameterSymbol typeParam)
    {
        if (SymbolEqualityComparer.Default.Equals(type, typeParam)) return true;
        if (type is INamedTypeSymbol named)
            return named.TypeArguments.Any(arg => ReferencesTypeParam(arg, typeParam));
        return false;
    }

    internal static TsAccessibility MapAccessibility(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Private => TsAccessibility.Private,
        Accessibility.Protected or Accessibility.ProtectedOrInternal => TsAccessibility.Protected,
        _ => TsAccessibility.Public,
    };



    private ExpressionTransformer CreateExpressionTransformer(SemanticModel semanticModel) =>
        _context!.CreateExpressionTransformer(semanticModel);

}
