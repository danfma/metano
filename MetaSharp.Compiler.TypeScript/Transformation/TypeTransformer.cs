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
        _externalImportMap = new Dictionary<string, (string Name, string From)>();
        foreach (var t in transpilableTypes.ToList())
        {
            var import = SymbolHelper.GetImport(t);
            if (import is not null)
            {
                _externalImportMap[t.Name] = import.Value;
                var tsName = GetTsTypeName(t);
                if (tsName != t.Name)
                    _externalImportMap[tsName] = import.Value;
            }
        }

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

        _context = new TypeScriptTransformContext(
            compilation,
            _currentAssembly,
            _assemblyWideTranspile,
            _transpilableTypeMap,
            _externalImportMap,
            _bclExportMap,
            _guardNameToTypeMap,
            _pathNaming,
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

        return files;
    }

    private bool _assemblyWideTranspile;
    private IAssemblySymbol? _currentAssembly;
    private Dictionary<string, INamedTypeSymbol> _transpilableTypeMap = [];
    private Dictionary<string, (string Name, string From)> _externalImportMap = [];
    private Dictionary<string, (string ExportedName, string FromPackage)> _bclExportMap = [];
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
            TransformAsModule(type, statements);
        }
        else if (TransformInlineWrapper(type, statements))
        {
            // InlineWrapper handled by specialized pipeline.
        }
        else if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
        {
            TransformRecordOrClass(type, statements);
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

    /// <summary>
    /// Collects transpilable interfaces implemented by a type, returning their TS names.
    /// </summary>
    private List<TsType> GetImplementedInterfaces(INamedTypeSymbol type)
    {
        var result = new List<TsType>();
        foreach (var iface in type.Interfaces)
        {
            if (SymbolHelper.IsTranspilable(iface.OriginalDefinition, _context!.AssemblyWideTranspile, _context.CurrentAssembly))
            {
                var tsName = GetTsTypeName(iface.OriginalDefinition);
                if (iface.TypeArguments.Length > 0)
                {
                    var args = iface.TypeArguments.Select(TypeMapper.Map).ToList();
                    result.Add(new TsNamedType(tsName, args));
                }
                else
                {
                    result.Add(new TsNamedType(tsName));
                }
            }
        }
        return result;
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

    // ─── ExportedAsModule (static class → top-level functions) ─

    private bool TransformInlineWrapper(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        if (!SymbolHelper.HasInlineWrapper(type))
            return false;
        if (type.TypeKind != TypeKind.Struct)
            return false;

        var tsTypeName = GetTsTypeName(type);
        if (!TryGetInlineWrapperPrimitiveType(type, out var primitiveType))
            return false;

        // export type UserId = string & { readonly __brand: "UserId" };
        var brandType = new TsNamedType($"{{ readonly __brand: \"{tsTypeName}\" }}");
        statements.Add(new TsTypeAlias(tsTypeName, new TsIntersectionType([primitiveType, brandType])));

        // Build companion namespace functions
        var functions = new List<TsFunction>();

        // create(value: T): TypeName
        functions.Add(new TsFunction(
            "create",
            [new TsParameter("value", primitiveType)],
            new TsNamedType(tsTypeName),
            [new TsReturnStatement(new TsCastExpression(new TsIdentifier("value"), new TsNamedType(tsTypeName)))],
            Exported: true
        ));

        // toString(value: TypeName): string — only for non-string primitives
        if (primitiveType is not TsStringType)
        {
            functions.Add(new TsFunction(
                "toString",
                [new TsParameter("value", new TsNamedType(tsTypeName))],
                new TsStringType(),
                [new TsReturnStatement(new TsCallExpression(new TsIdentifier("String"), [new TsIdentifier("value")]))],
                Exported: true
            ));
        }

        // Static methods from the struct
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary) continue;
            if (!method.IsStatic) continue;
            if (method.IsImplicitlyDeclared) continue;
            if (method.DeclaredAccessibility != Accessibility.Public) continue;
            if (SymbolHelper.HasIgnore(method)) continue;
            if (TypeScriptNaming.HasEmit(method)) continue;

            var methodSyntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            if (methodSyntax is null) continue;

            var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            var exprTransformer = CreateExpressionTransformer(semanticModel);
            var body = exprTransformer.TransformBody(methodSyntax.Body, methodSyntax.ExpressionBody,
                isVoid: method.ReturnsVoid);
            var parameters = method.Parameters
                .Select(p => new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
                .ToList();

            var methodName = SymbolHelper.GetNameOverride(method) ?? TypeScriptNaming.ToCamelCase(method.Name);
            var returnType = TypeMapper.Map(method.ReturnType);
            functions.Add(new TsFunction(methodName, parameters, returnType, body,
                Exported: true, Async: method.IsAsync));
        }

        // export namespace TypeName { ... }
        statements.Add(new TsNamespaceDeclaration(tsTypeName, functions));
        return true;
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

    private void TransformAsModule(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // Process direct members (classic extension methods, plain static functions)
        foreach (var member in type.GetMembers())
        {
            if (SymbolHelper.HasIgnore(member)) continue;

            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    var func = TransformModuleFunction(type, method);
                    if (func is not null) statements.Add(func);
                    break;

                // Extension properties on classic style (parameters via Roslyn)
                case IPropertySymbol prop when prop.Parameters.Length > 0:
                    var propFunc = TransformExtensionProperty(type, prop);
                    if (propFunc is not null) statements.Add(propFunc);
                    break;
            }
        }

        // Process C# 14 extension blocks via syntax tree
        // (Roslyn exposes them as nested anonymous types with TypeKind=Extension,
        //  but it's simpler to walk the syntax directly)
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            foreach (var node in syntax.DescendantNodes())
            {
                if (node.Kind().ToString() != "ExtensionBlockDeclaration") continue;
                TransformExtensionBlock(node, statements);
            }
        }
    }

    /// <summary>
    /// Transforms a C# 14 extension block syntax into top-level functions.
    /// The block syntax is: `extension(Type receiver) { members... }`
    /// </summary>
    private void TransformExtensionBlock(SyntaxNode extensionBlock, List<TsTopLevel> statements)
    {
        // ExtensionBlockDeclarationSyntax has ParameterList and Members
        var paramListProp = extensionBlock.GetType().GetProperty("ParameterList");
        var membersProp = extensionBlock.GetType().GetProperty("Members");
        if (paramListProp?.GetValue(extensionBlock) is not ParameterListSyntax paramList) return;
        if (membersProp?.GetValue(extensionBlock) is not SyntaxList<MemberDeclarationSyntax> members) return;
        if (paramList.Parameters.Count == 0) return;

        var receiverParamSyntax = paramList.Parameters[0];
        var semanticModel = compilation.GetSemanticModel(extensionBlock.SyntaxTree);

        var receiverName = TypeScriptNaming.ToCamelCase(receiverParamSyntax.Identifier.Text);
        var receiverTypeSymbol = receiverParamSyntax.Type is null
            ? null
            : semanticModel.GetTypeInfo(receiverParamSyntax.Type).Type;
        if (receiverTypeSymbol is null) return;
        var receiverType = TypeMapper.Map(receiverTypeSymbol);
        var receiverParam = new TsParameter(receiverName, receiverType);

        var exprTransformer = CreateExpressionTransformer(semanticModel);

        foreach (var member in members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax methodSyntax:
                {
                    var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax) as IMethodSymbol;
                    if (methodSymbol is null) continue;
                    if (methodSymbol.DeclaredAccessibility != Accessibility.Public) continue;

                    var name = SymbolHelper.GetNameOverride(methodSymbol)
                        ?? TypeScriptNaming.ToCamelCase(methodSymbol.Name);
                    var returnType = TypeMapper.Map(methodSymbol.ReturnType);
                    var parameters = new List<TsParameter> { receiverParam };
                    parameters.AddRange(methodSymbol.Parameters.Select(p =>
                        new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type))));

                    var body = exprTransformer.TransformBody(methodSyntax.Body, methodSyntax.ExpressionBody,
                        isVoid: methodSymbol.ReturnsVoid);
                    statements.Add(new TsFunction(name, parameters, returnType, body, Exported: true));
                    break;
                }
                case PropertyDeclarationSyntax propSyntax:
                {
                    var propSymbol = semanticModel.GetDeclaredSymbol(propSyntax) as IPropertySymbol;
                    if (propSymbol is null) continue;
                    if (propSymbol.DeclaredAccessibility != Accessibility.Public) continue;

                    var name = SymbolHelper.GetNameOverride(propSymbol)
                        ?? TypeScriptNaming.ToCamelCase(propSymbol.Name);
                    var returnType = TypeMapper.Map(propSymbol.Type);
                    var parameters = new List<TsParameter> { receiverParam };

                    IReadOnlyList<TsStatement> body;
                    if (propSyntax.ExpressionBody is not null)
                        body = [new TsReturnStatement(exprTransformer.TransformExpression(propSyntax.ExpressionBody.Expression))];
                    else if (propSyntax.AccessorList is not null)
                    {
                        var getAccessor = propSyntax.AccessorList.Accessors
                            .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                        if (getAccessor is null) continue;
                        body = exprTransformer.TransformBody(getAccessor.Body, getAccessor.ExpressionBody);
                    }
                    else continue;

                    statements.Add(new TsFunction(name, parameters, returnType, body, Exported: true));
                    break;
                }
            }
        }
    }

    private TsFunction? TransformExtensionProperty(INamedTypeSymbol containingType, IPropertySymbol prop)
    {
        if (prop.DeclaredAccessibility != Accessibility.Public) return null;
        if (prop.IsImplicitlyDeclared) return null;

        var name = SymbolHelper.GetNameOverride(prop) ?? TypeScriptNaming.ToCamelCase(prop.Name);
        var returnType = TypeMapper.Map(prop.Type);

        // The receiver parameter
        var parameters = prop.Parameters
            .Select(p => new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
            .ToList();

        // Get the getter body
        var getter = prop.GetMethod;
        if (getter is null) return null;

        var syntax = getter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var semanticModel = compilation.GetSemanticModel(syntax!.SyntaxTree);
        var exprTransformer = CreateExpressionTransformer(semanticModel);

        IReadOnlyList<TsStatement> body;
        if (syntax is AccessorDeclarationSyntax accessor)
            body = exprTransformer.TransformBody(accessor.Body, accessor.ExpressionBody);
        else if (syntax is ArrowExpressionClauseSyntax arrow)
            body = [new TsReturnStatement(exprTransformer.TransformExpression(arrow.Expression))];
        else
            return null;

        return new TsFunction(name, parameters, returnType, body);
    }

    private TsFunction? TransformModuleFunction(INamedTypeSymbol containingType, IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public) return null;
        if (method.IsImplicitlyDeclared) return null;
        if (TypeScriptNaming.HasEmit(method)) return null;
        // Skip property accessors — extension properties are handled via their associated property
        if (method.AssociatedSymbol is IPropertySymbol) return null;

        var syntaxNode = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntaxNode is null) return null;

        var name = SymbolHelper.GetNameOverride(method) ?? TypeScriptNaming.ToCamelCase(method.Name);
        var hasYield = syntaxNode.DescendantNodes().OfType<YieldStatementSyntax>().Any();
        var returnType = hasYield
            ? TypeMapper.MapForGeneratorReturn(method.ReturnType)
            : TypeMapper.Map(method.ReturnType);
        var isAsync = hasYield ? false : method.IsAsync;

        var parameters = method.Parameters
            .Select(p => new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
            .ToList();

        var semanticModel = compilation.GetSemanticModel(syntaxNode.SyntaxTree);
        var exprTransformer = CreateExpressionTransformer(semanticModel);

        IReadOnlyList<TsStatement> body;
        if (syntaxNode is MethodDeclarationSyntax methodSyntax)
            body = exprTransformer.TransformBody(methodSyntax.Body, methodSyntax.ExpressionBody);
        else if (syntaxNode is ArrowExpressionClauseSyntax arrow)
            body = [new TsReturnStatement(exprTransformer.TransformExpression(arrow.Expression))];
        else
            return null;

        return new TsFunction(name, parameters, returnType, body, Exported: true, Async: isAsync,
            Generator: hasYield,
            TypeParameters: ExtractMethodTypeParameters(method));
    }

    // ─── Record / Struct / Class ────────────────────────────

    private void TransformRecordOrClass(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // Resolve base class (if transpilable)
        TsType? extendsType = null;
        var baseParams = Array.Empty<TsConstructorParam>();

        if (type.BaseType is not null
            && type.BaseType.SpecialType == SpecialType.None
            && type.BaseType.ToDisplayString() != "System.Object"
            && type.BaseType.ToDisplayString() != "System.ValueType"
            && SymbolHelper.IsTranspilable(type.BaseType.OriginalDefinition, _context!.AssemblyWideTranspile, _context.CurrentAssembly))
        {
            extendsType = TypeMapper.Map(type.BaseType);
            baseParams = GetConstructorParams(type.BaseType.OriginalDefinition).ToArray();
        }

        var ownParams = GetOwnConstructorParams(type);
        // All params for equals/hashCode/with (conceptual fields — both inherited and own)
        var allParams = baseParams.Concat(ownParams).ToList();
        // Constructor signature: only own params (base properties are declared in parent)
        var ctorParamsForSignature = ownParams.ToList();

        // Detect captured primary constructor params (used in field initializers but not properties)
        var capturedParams = GetCapturedConstructorParams(type, ctorParamsForSignature);
        ctorParamsForSignature.AddRange(capturedParams);
        var capturedParamNames = capturedParams.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Detect multiple constructors
        var explicitCtors = type.Constructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length > 0)
            .ToList();

        TsConstructor constructor;

        if (explicitCtors.Count > 1)
        {
            constructor = new OverloadDispatcherBuilder(_context!)
                .BuildConstructor(type, explicitCtors, extendsType);
        }
        else
        {
            // Single constructor (original behavior)
            var ctorBody = new List<TsStatement>();
            if (extendsType is not null)
            {
                var superArgs = ResolveSuperArguments(type, baseParams);
                if (superArgs.Count > 0)
                {
                    ctorBody.Add(new TsExpressionStatement(
                        new TsCallExpression(new TsIdentifier("super"), superArgs)
                    ));
                }
            }

            // Add captured param assignments: this._field = param
            foreach (var captured in capturedParams)
            {
                var fieldName = GetCapturedFieldName(type, captured.Name);
                if (fieldName is not null)
                {
                    ctorBody.Add(new TsExpressionStatement(
                        new TsBinaryExpression(
                            new TsPropertyAccess(new TsIdentifier("this"), fieldName),
                            "=",
                            new TsIdentifier(captured.Name))));
                }
            }

            constructor = new TsConstructor(ctorParamsForSignature, ctorBody);
        }
        var classMembers = new List<TsClassMember>();

        // Fields, properties, operators
        var ordinaryMethods = new List<IMethodSymbol>();

        foreach (var member in type.GetMembers())
        {
            if (SymbolHelper.HasIgnore(member))
                continue;

            switch (member)
            {
                case IFieldSymbol field:
                    var fieldMember = TransformField(type, field, capturedParamNames);
                    if (fieldMember is not null)
                        classMembers.Add(fieldMember);
                    break;

                case IPropertySymbol prop when !IsConstructorParam(prop, ctorParamsForSignature):
                    var propMembers = TransformProperty(type, prop);
                    classMembers.AddRange(propMembers);
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                    && !method.IsImplicitlyDeclared
                    && method.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.NotApplicable)
                    && !TypeScriptNaming.HasEmit(method)
                    && method.AssociatedSymbol is not IPropertySymbol:
                    ordinaryMethods.Add(method);
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.UserDefinedOperator:
                    classMembers.AddRange(TransformClassOperator(type, method));
                    break;
            }
        }

        // Process ordinary methods — detect overloads (same name, different signatures)
        var methodGroups = ordinaryMethods
            .GroupBy(m => m.Name)
            .ToList();

        foreach (var group in methodGroups)
        {
            var methods = group.ToList();
            if (methods.Count == 1)
            {
                // Single method — no dispatcher needed
                var classMember = TransformClassMethod(type, methods[0]);
                if (classMember is not null)
                    classMembers.Add(classMember);
            }
            else
            {
                // Multiple overloads — generate dispatcher
                var overloadMembers = new OverloadDispatcherBuilder(_context!)
                    .BuildMethod(type, methods);
                classMembers.AddRange(overloadMembers);
            }
        }

        // Generate equals, hashCode, with for records (using ALL params including inherited)
        if (type.IsRecord)
        {
            classMembers.Add(RecordSynthesizer.GenerateEquals(type, allParams));
            classMembers.Add(RecordSynthesizer.GenerateHashCode(allParams));
            classMembers.Add(RecordSynthesizer.GenerateWith(type, allParams));
        }

        var implementsList = GetImplementedInterfaces(type);
        var typeParams = ExtractTypeParameters(type);

        statements.Add(new TsClass(
            type.Name,
            constructor,
            classMembers,
            Extends: extendsType,
            Implements: implementsList.Count > 0 ? implementsList : null,
            TypeParameters: typeParams
        ));
    }

    private IReadOnlyList<TsConstructorParam> GetConstructorParams(INamedTypeSymbol type)
    {
        var primaryCtorParamDefaults = GetPrimaryConstructorParamDefaults(type);
        var ctorParams = new List<TsConstructorParam>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) continue;
            if (SymbolHelper.HasIgnore(member)) continue;
            if (!primaryCtorParamDefaults.ContainsKey(member.Name)) continue;

            var name = SymbolHelper.GetNameOverride(member) ?? TypeScriptNaming.ToCamelCase(member.Name);
            var tsType = TypeMapper.Map(member.Type);
            var isReadonly = member.SetMethod is null || member.SetMethod.IsInitOnly;
            var accessibility = MapAccessibility(member.DeclaredAccessibility);
            var defaultValue = primaryCtorParamDefaults[member.Name];

            ctorParams.Add(new TsConstructorParam(name, tsType, isReadonly, accessibility, defaultValue));
        }

        return ctorParams;
    }

    /// <summary>
    /// Returns only properties declared directly on this type (not inherited).
    /// </summary>
    private IReadOnlyList<TsConstructorParam> GetOwnConstructorParams(INamedTypeSymbol type)
    {
        var primaryCtorParamDefaults = GetPrimaryConstructorParamDefaults(type);
        var ctorParams = new List<TsConstructorParam>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) continue;
            if (SymbolHelper.HasIgnore(member)) continue;
            if (member.IsOverride) continue;
            if (!primaryCtorParamDefaults.ContainsKey(member.Name)) continue;

            var name = SymbolHelper.GetNameOverride(member) ?? TypeScriptNaming.ToCamelCase(member.Name);
            var tsType = TypeMapper.Map(member.Type);
            var isReadonly = member.SetMethod is null || member.SetMethod.IsInitOnly;
            var accessibility = MapAccessibility(member.DeclaredAccessibility);
            var defaultValue = primaryCtorParamDefaults[member.Name];

            ctorParams.Add(new TsConstructorParam(name, tsType, isReadonly, accessibility, defaultValue));
        }

        return ctorParams;
    }

    /// <summary>
    /// Gets the parameter names of the primary constructor (record or class).
    /// </summary>
    /// <summary>
    /// Gets the primary constructor parameter names and their default values (if any).
    /// Includes all constructors — for C# 12+ primary constructor classes,
    /// the constructor is IsImplicitlyDeclared (unlike records where it's explicit).
    /// </summary>
    private Dictionary<string, TsExpression?> GetPrimaryConstructorParamDefaults(INamedTypeSymbol type)
    {
        var result = new Dictionary<string, TsExpression?>(StringComparer.OrdinalIgnoreCase);
        var primaryCtor = type.Constructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (primaryCtor is null) return result;

        foreach (var p in primaryCtor.Parameters)
        {
            TsExpression? defaultValue = null;
            if (p.HasExplicitDefaultValue)
            {
                // Check if the parameter type is a StringEnum — resolve to string literal
                if (p.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                    && SymbolHelper.HasStringEnum(enumType)
                    && p.ExplicitDefaultValue is int enumOrdinal)
                {
                    var enumMember = enumType.GetMembers().OfType<IFieldSymbol>()
                        .Where(f => f.HasConstantValue)
                        .FirstOrDefault(f => (int)f.ConstantValue! == enumOrdinal);

                    if (enumMember is not null)
                    {
                        var memberName = SymbolHelper.GetNameOverride(enumMember) ?? enumMember.Name;
                        defaultValue = new TsStringLiteral(memberName);
                    }
                    else
                    {
                        defaultValue = new TsLiteral(enumOrdinal.ToString());
                    }
                }
                else
                {
                    defaultValue = p.ExplicitDefaultValue switch
                    {
                        null => new TsLiteral("null"),
                        string s => new TsStringLiteral(s),
                        bool b => new TsLiteral(b ? "true" : "false"),
                        int i => new TsLiteral(i.ToString()),
                        long l => new TsLiteral(l.ToString()),
                        double d => new TsLiteral(d.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        _ => new TsLiteral(p.ExplicitDefaultValue.ToString()!)
                    };
                }
            }

            result[p.Name] = defaultValue;
        }

        return result;
    }

    /// <summary>
    /// Checks if a property is already represented as a constructor parameter.
    /// </summary>
    /// <summary>
    /// Transforms a C# field into a TsFieldMember.
    /// Handles private/protected/public fields with initializers.
    /// </summary>
    private TsFieldMember? TransformField(INamedTypeSymbol containingType, IFieldSymbol field,
        HashSet<string>? capturedParamNames = null)
    {
        if (field.IsImplicitlyDeclared) return null;
        if (field.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) return null;
        if (SymbolHelper.HasIgnore(field)) return null;
        // Skip backing fields for auto-properties (compiler-generated)
        if (field.AssociatedSymbol is not null) return null;

        var name = SymbolHelper.GetNameOverride(field) ?? TypeScriptNaming.ToCamelCase(field.Name);
        var tsType = TypeMapper.Map(field.Type);
        var isReadonly = field.IsReadOnly;
        var accessibility = MapAccessibility(field.DeclaredAccessibility);

        // Try to get initializer from syntax
        TsExpression? initializer = null;
        var syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is VariableDeclaratorSyntax { Initializer: not null } varDecl)
        {
            // Skip initializer if it references a captured constructor param
            // (the assignment is moved to the constructor body)
            var isCapuredInit = varDecl.Initializer.Value is IdentifierNameSyntax initId
                && capturedParamNames is not null
                && capturedParamNames.Contains(TypeScriptNaming.ToCamelCase(initId.Identifier.Text));

            if (!isCapuredInit)
            {
                var semanticModel = compilation.GetSemanticModel(varDecl.SyntaxTree);
                var exprTransformer = CreateExpressionTransformer(semanticModel);
                initializer = exprTransformer.TransformExpression(varDecl.Initializer.Value);
            }
        }

        return new TsFieldMember(name, tsType, initializer, isReadonly, accessibility);
    }

    private static bool IsConstructorParam(IPropertySymbol prop, IReadOnlyList<TsConstructorParam> ctorParams)
    {
        var name = SymbolHelper.GetNameOverride(prop) ?? TypeScriptNaming.ToCamelCase(prop.Name);
        return ctorParams.Any(p => p.Name == name);
    }

    /// <summary>
    /// Transforms a non-constructor property into getter/setter/field AST members.
    /// </summary>
    private IReadOnlyList<TsClassMember> TransformProperty(
        INamedTypeSymbol containingType,
        IPropertySymbol prop)
    {
        if (prop.IsImplicitlyDeclared) return [];
        if (prop.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) return [];
        if (SymbolHelper.HasIgnore(prop)) return [];

        var name = SymbolHelper.GetNameOverride(prop) ?? TypeScriptNaming.ToCamelCase(prop.Name);
        var tsType = TypeMapper.Map(prop.Type);
        var accessibility = MapAccessibility(prop.DeclaredAccessibility);
        var results = new List<TsClassMember>();

        // Check if the property has explicit accessor bodies
        var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;

        var hasGetterBody = syntax?.ExpressionBody is not null
            || syntax?.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration) && (a.Body is not null || a.ExpressionBody is not null)) == true;
        var hasSetterBody = syntax?.AccessorList?.Accessors.Any(a =>
            a.IsKind(SyntaxKind.SetAccessorDeclaration) && (a.Body is not null || a.ExpressionBody is not null)) == true;

        if (hasGetterBody || syntax?.ExpressionBody is not null)
        {
            // Computed property → getter
            var semanticModel = compilation.GetSemanticModel(syntax!.SyntaxTree);
            var exprTransformer = CreateExpressionTransformer(semanticModel); exprTransformer.SelfParameterName = "this";

            IReadOnlyList<TsStatement> getterBody;
            if (syntax.ExpressionBody is not null)
            {
                getterBody = [new TsReturnStatement(exprTransformer.TransformExpression(syntax.ExpressionBody.Expression))];
            }
            else
            {
                var getAccessor = syntax.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                getterBody = exprTransformer.TransformBody(getAccessor.Body, getAccessor.ExpressionBody);
            }

            results.Add(new TsGetterMember(name, tsType, getterBody));
        }

        if (hasSetterBody)
        {
            var setAccessor = syntax!.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            var exprTransformer = CreateExpressionTransformer(semanticModel); exprTransformer.SelfParameterName = "this";
            var setterBody = exprTransformer.TransformBody(setAccessor.Body, setAccessor.ExpressionBody);
            var valueParam = new TsParameter("value", tsType);

            results.Add(new TsSetterMember(name, valueParam, setterBody));
        }

        // Auto-property (no custom bodies) that isn't a ctor param → field
        if (!hasGetterBody && syntax?.ExpressionBody is null)
        {
            var isReadonly = prop.SetMethod is null || prop.SetMethod.IsInitOnly;
            TsExpression? initializer = null;

            if (syntax?.Initializer is not null)
            {
                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var exprTransformer = CreateExpressionTransformer(semanticModel); exprTransformer.SelfParameterName = "this";
                initializer = exprTransformer.TransformExpression(syntax.Initializer.Value);
            }
            else if (prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                     || prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                // Nullable properties without explicit initializer → = null
                initializer = new TsLiteral("null");
            }

            results.Add(new TsFieldMember(name, tsType, initializer, isReadonly, accessibility));
        }

        return results;
    }

    private TsClassMember? TransformClassMethod(
        INamedTypeSymbol containingType,
        IMethodSymbol method
    )
    {
        if (method.IsImplicitlyDeclared)
            return null;
        // [Emit] methods are consumed inline at call sites, not generated as methods
        if (TypeScriptNaming.HasEmit(method))
            return null;
        // Skip compiler-generated or internal/unsupported access levels
        if (method.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable)
            return null;

        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (syntax is null)
            return null;

        var name = SymbolHelper.GetNameOverride(method) ?? TypeScriptNaming.ToCamelCase(method.Name);
        var hasYield = syntax.DescendantNodes().OfType<YieldStatementSyntax>().Any();
        var returnType = hasYield
            ? TypeMapper.MapForGeneratorReturn(method.ReturnType)
            : TypeMapper.Map(method.ReturnType);
        var isAsync = hasYield ? false : method.IsAsync;

        var parameters = method
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                TypeMapper.Map(p.Type)
            ))
            .ToList();

        var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
        var exprTransformer = CreateExpressionTransformer(semanticModel);

        // Instance methods use 'this' — set self parameter name to "this"
        if (!method.IsStatic)
            exprTransformer.SelfParameterName = "this";

        var body = exprTransformer.TransformBody(syntax.Body, syntax.ExpressionBody, isVoid: method.ReturnsVoid);

        return new TsMethodMember(
            name,
            parameters,
            returnType,
            body,
            Static: method.IsStatic,
            Async: isAsync,
            Generator: hasYield,
            Accessibility: MapAccessibility(method.DeclaredAccessibility),
            TypeParameters: ExtractMethodTypeParameters(method)
        );
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

    /// <summary>
    /// Detects primary constructor parameters that are captured in field initializers
    /// but are not properties (e.g., DI params like `IssueService(IIssueRepository repository)`).
    /// These need to be added to the TS constructor signature.
    /// </summary>
    private IReadOnlyList<TsConstructorParam> GetCapturedConstructorParams(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> existingParams)
    {
        var primaryCtor = type.Constructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        if (primaryCtor is null) return [];

        var existingNames = existingParams.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<TsConstructorParam>();

        foreach (var param in primaryCtor.Parameters)
        {
            var camelName = TypeScriptNaming.ToCamelCase(param.Name);
            if (existingNames.Contains(camelName)) continue;

            // Check if this param is referenced by any field initializer
            var isCapured = type.GetMembers().OfType<IFieldSymbol>()
                .Any(f =>
                {
                    var syntax = f.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (syntax is VariableDeclaratorSyntax { Initializer.Value: IdentifierNameSyntax id })
                        return string.Equals(id.Identifier.Text, param.Name, StringComparison.OrdinalIgnoreCase);
                    return false;
                });

            if (isCapured)
            {
                result.Add(new TsConstructorParam(
                    camelName,
                    TypeMapper.Map(param.Type),
                    Accessibility: TsAccessibility.None));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the TS field name for a captured constructor param (e.g., "repository" → "_repository").
    /// </summary>
    private static string? GetCapturedFieldName(INamedTypeSymbol type, string paramName)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            var syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (syntax is VariableDeclaratorSyntax { Initializer.Value: IdentifierNameSyntax id }
                && string.Equals(id.Identifier.Text, paramName, StringComparison.OrdinalIgnoreCase))
            {
                return TypeScriptNaming.ToCamelCase(field.Name);
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

    private IReadOnlyList<TsClassMember> TransformClassOperator(
        INamedTypeSymbol containingType,
        IMethodSymbol method
    )
    {
        var nameOverride = SymbolHelper.GetNameOverride(method);
        if (nameOverride is null)
            return [];

        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as OperatorDeclarationSyntax;
        if (syntax is null)
            return [];

        var returnType = TypeMapper.Map(method.ReturnType);

        var parameters = method
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                TypeMapper.Map(p.Type)
            ))
            .ToList();

        var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
        var exprTransformer = CreateExpressionTransformer(semanticModel);
        var body = exprTransformer.TransformBody(syntax.Body, syntax.ExpressionBody);

        var staticName = $"__{nameOverride}";
        var isUnary = method.Parameters.Length == 1;
        var results = new List<TsClassMember>();

        // Static operator method: static __add(left, right) or static __negate(operand)
        results.Add(new TsMethodMember(staticName, parameters, returnType, body, Static: true));

        // Instance helper: $add(right) or $negate()
        if (isUnary)
        {
            // Unary: $negate(): Type { return ClassName.__negate(this); }
            var helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier(containingType.Name), staticName),
                    [new TsIdentifier("this")]
                )
            );
            results.Add(new TsMethodMember($"${nameOverride}", [], returnType, [helperBody]));
        }
        else
        {
            // Binary: $add(right): Type { return ClassName.__add(this, right); }
            var rightParam = parameters.Last();
            var helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier(containingType.Name), staticName),
                    [new TsIdentifier("this"), new TsIdentifier(rightParam.Name)]
                )
            );
            results.Add(new TsMethodMember($"${nameOverride}", [rightParam], returnType, [helperBody]));
        }

        return results;
    }

    // ─── Record generated members ───────────────────────────

    /// <summary>
    /// Resolves the actual arguments passed to the base constructor.
    /// For `record Ok(T Value) : Result(Value, true)`, returns [Identifier("value"), Literal("true")].
    /// Falls back to passing base param names if syntax can't be resolved.
    /// </summary>
    private IReadOnlyList<TsExpression> ResolveSuperArguments(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> baseParams)
    {
        // Try to find PrimaryConstructorBaseTypeSyntax in the type's declaration
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl) continue;
            if (typeDecl.BaseList is null) continue;

            foreach (var baseType in typeDecl.BaseList.Types)
            {
                if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
                {
                    var semanticModel = compilation.GetSemanticModel(primaryBase.SyntaxTree);
                    var exprTransformer = CreateExpressionTransformer(semanticModel);
                    return primaryBase.ArgumentList.Arguments
                        .Select(a => exprTransformer.TransformExpression(a.Expression))
                        .ToList();
                }
            }
        }

        // Fallback: pass base param names
        return baseParams
            .Select<TsConstructorParam, TsExpression>(p => new TsIdentifier(p.Name))
            .ToList();
    }

    private ExpressionTransformer CreateExpressionTransformer(SemanticModel semanticModel) =>
        _context!.CreateExpressionTransformer(semanticModel);

}
