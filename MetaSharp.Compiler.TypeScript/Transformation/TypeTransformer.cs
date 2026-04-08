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
            var nestedFile = TransformType(nestedType);
            if (nestedFile is null) continue;

            // Skip imports — they're already handled by the parent file's CollectImports.
            // Recursively flatten nested types of nested types is handled by the recursion.
            foreach (var stmt in nestedFile.Statements)
            {
                if (stmt is TsImport or TsReExport) continue;
                members.Add(stmt);
            }
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
        _externalImportMap = new Dictionary<string, (string Name, string From, bool IsDefault)>();
        foreach (var t in transpilableTypes.ToList())
        {
            var import = SymbolHelper.GetImport(t);
            if (import is not null)
            {
                var entry = (import.Name, import.From, import.AsDefault);
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

        foreach (var type in transpilableTypes)
        {
            var file = TransformType(type);
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

        return files;
    }

    private bool _assemblyWideTranspile;
    private IAssemblySymbol? _currentAssembly;
    private Dictionary<string, INamedTypeSymbol> _transpilableTypeMap = [];
    private Dictionary<string, (string Name, string From, bool IsDefault)> _externalImportMap = [];
    private Dictionary<string, (string ExportedName, string FromPackage)> _bclExportMap = [];
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
            // we have no package name to import from. (The consumer-side error MS0007
            // is raised in 21d when the type is actually referenced.)
            var packageName = SymbolHelper.GetEmitPackage(asm, targetEnumValue: 0);
            if (packageName is null) continue;

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
                    var entry = (import.Name, import.From, import.AsDefault);
                    _externalImportMap[type.Name] = entry;
                    var tsName = GetTsTypeName(type);
                    if (tsName != type.Name)
                        _externalImportMap[tsName] = entry;
                    continue;
                }

                _crossAssemblyTypeMap[type] = new CrossAssemblyEntry(type, packageName, rootNs);
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
        _bclExportMap = new Dictionary<string, (string ExportedName, string FromPackage)>();

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("ExportFromBclAttribute" or "ExportFromBcl"))
                continue;

            // Constructor arg is typeof(Type)
            if (attr.ConstructorArguments.Length == 0) continue;
            var typeArg = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (typeArg is null) continue;

            var exportedName = "";
            var fromPackage = "";

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
                }
            }

            if (exportedName.Length > 0)
            {
                _bclExportMap[typeArg.ToDisplayString()] = (exportedName, fromPackage);
            }
        }

        // Make BCL export map available to TypeMapper
        TypeMapper.BclExportMap = _bclExportMap;
    }

    private TsSourceFile? TransformType(INamedTypeSymbol type)
    {
        // [Import] types are external — don't generate .ts files
        if (SymbolHelper.HasImport(type))
            return null;

        // [NoEmit] types are ambient/declaration-only — discoverable in C# so consumers
        // can reference them in signatures, but no .ts file is generated and no import
        // is emitted. Used for structural shapes over external library types.
        if (SymbolHelper.HasNoEmit(type))
            return null;

        var statements = new List<TsTopLevel>();

        if (type.TypeKind == TypeKind.Enum)
        {
            EnumTransformer.Transform(type, statements);
        }
        else if (type.TypeKind == TypeKind.Interface)
        {
            InterfaceTransformer.Transform(type, statements);
        }
        else if (IsExceptionType(type))
        {
            new ExceptionTransformer(_context!).Transform(type, statements);
        }
        else if ((SymbolHelper.HasExportedAsModule(type) || HasExtensionMembers(type)) && type.IsStatic)
        {
            new ModuleTransformer(_context!).Transform(type, statements);
        }
        else if (new InlineWrapperTransformer(_context!).Transform(type, statements))
        {
            // InlineWrapper handled by specialized pipeline.
        }
        else if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
        {
            new RecordClassTransformer(_context!).Transform(type, statements);
        }

        if (statements.Count == 0)
            return null;

        // Generate type guard function when [GenerateGuard] is present
        if (SymbolHelper.HasGenerateGuard(type))
        {
            var guard = new TypeGuardBuilder(_context!.TranspilableTypeMap).Generate(type);
            if (guard is not null)
                statements.Add(guard);
        }

        // Process nested types — emit a companion namespace with the nested members.
        // TypeScript declaration merging makes `Outer.Inner` accessible just like in C#.
        TransformNestedTypes(type, statements);

        // Add imports for referenced transpilable types
        var imports = new ImportCollector(
            _context!.TranspilableTypeMap,
            _context.ExternalImportMap,
            _context.BclExportMap,
            _context.GuardNameToTypeMap,
            _context.PathNaming)
            .Collect(type, statements);
        statements.InsertRange(0, imports);

        var ns = PathNaming.GetNamespace(type);
        var tsTypeName = GetTsTypeName(type);
        var relativePath = _context!.PathNaming.GetRelativePath(ns, tsTypeName);

        return new TsSourceFile(relativePath, statements, ns);
    }

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
