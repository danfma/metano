using MetaSharp.Diagnostics;
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
            .Where(t => SymbolHelper.IsTranspilable(t, _assemblyWideTranspile, _currentAssembly))
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
            .Select(t => GetNamespace(t))
            .Where(ns => ns.Length > 0)
            .ToList();

        _rootNamespace = namespaces.Count > 0 ? FindCommonNamespacePrefix(namespaces) : "";

        var files = new List<TsSourceFile>();

        foreach (var type in transpilableTypes)
        {
            var file = TransformType(type);
            if (file is not null)
                files.Add(file);
        }

        // Generate index.ts barrel files per namespace folder
        var indexFiles = GenerateIndexFiles(files);
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
    private string _rootNamespace = "";

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
            TransformEnum(type, statements);
        }
        else if (type.TypeKind == TypeKind.Interface)
        {
            TransformInterface(type, statements);
        }
        else if (IsExceptionType(type))
        {
            TransformException(type, statements);
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
            var guard = GenerateTypeGuard(type);
            if (guard is not null)
                statements.Add(guard);
        }

        // Process nested types — emit a companion namespace with the nested members.
        // TypeScript declaration merging makes `Outer.Inner` accessible just like in C#.
        TransformNestedTypes(type, statements);

        // Add imports for referenced transpilable types
        var imports = CollectImports(type, statements);
        statements.InsertRange(0, imports);

        var ns = GetNamespace(type);
        var tsTypeName = GetTsTypeName(type);
        var relativePath = GetRelativePath(ns, tsTypeName);

        return new TsSourceFile(relativePath, statements, ns);
    }

    // ─── Interface ──────────────────────────────────────────

    private void TransformInterface(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        var properties = new List<TsProperty>();
        var interfaceMethods = new List<TsInterfaceMethod>();

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (SymbolHelper.HasIgnore(member)) continue;

            switch (member)
            {
                case IPropertySymbol prop:
                    var propName = SymbolHelper.GetNameOverride(prop) ?? SymbolHelper.ToCamelCase(prop.Name);
                    var propType = TypeMapper.Map(prop.Type);
                    var isReadonly = prop.SetMethod is null || prop.SetMethod.IsInitOnly;
                    properties.Add(new TsProperty(propName, propType, isReadonly));
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    var name = SymbolHelper.GetNameOverride(method) ?? SymbolHelper.ToCamelCase(method.Name);
                    var returnType = TypeMapper.Map(method.ReturnType);
                    var parameters = method.Parameters
                        .Select(p => new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
                        .ToList();
                    var methodTypeParams = ExtractMethodTypeParameters(method);
                    interfaceMethods.Add(new TsInterfaceMethod(name, parameters, returnType, methodTypeParams));
                    break;
            }
        }

        // Strip 'I' prefix convention for TS (IShape → Shape)
        var tsName = GetTsTypeName(type);
        var typeParams = ExtractTypeParameters(type);
        statements.Add(new TsInterface(tsName, properties, TypeParameters: typeParams,
            Methods: interfaceMethods.Count > 0 ? interfaceMethods : null));
    }

    /// <summary>
    /// Returns the TypeScript name for a type. Uses [Name] override if present, otherwise the C# name as-is.
    /// </summary>
    private static string GetTsTypeName(INamedTypeSymbol type)
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
            if (SymbolHelper.IsTranspilable(iface.OriginalDefinition, _assemblyWideTranspile, _currentAssembly))
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

    // ─── Enum ───────────────────────────────────────────────

    private void TransformEnum(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        var isStringEnum = SymbolHelper.HasStringEnum(type);

        if (isStringEnum)
        {
            var literalTypes = new List<TsType>();
            var entries = new List<(string Key, TsExpression Value)>();
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!member.HasConstantValue)
                    continue;
                var name = SymbolHelper.GetNameOverride(member) ?? member.Name;
                literalTypes.Add(new TsStringLiteralType(name));
                entries.Add((member.Name, new TsStringLiteral(name)));
            }

            // Generate: export const EnumName = { Member: "value", ... } as const;
            statements.Add(new TsConstObject(type.Name, entries));
            // Generate: export type EnumName = typeof EnumName[keyof typeof EnumName];
            statements.Add(new TsTypeAlias(type.Name,
                new TsNamedType($"typeof {type.Name}[keyof typeof {type.Name}]")));
        }
        else
        {
            var members = new List<TsEnumMember>();
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!member.HasConstantValue)
                    continue;
                var name = SymbolHelper.GetNameOverride(member) ?? member.Name;
                members.Add(
                    new TsEnumMember(name, new TsLiteral(member.ConstantValue!.ToString()!))
                );
            }

            statements.Add(new TsEnum(type.Name, members));
        }
    }

    // ─── Exception (class extending Error) ───────────────────

    private void TransformException(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // Find the primary constructor or the constructor that calls base(message)
        var ctor = type.Constructors
            .Where(c => !c.IsImplicitlyDeclared && c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        var ctorParams = new List<TsConstructorParam>();
        var superArgs = new List<TsExpression>();

        if (ctor is not null)
        {
            foreach (var p in ctor.Parameters)
            {
                ctorParams.Add(new TsConstructorParam(
                    SymbolHelper.ToCamelCase(p.Name),
                    TypeMapper.Map(p.Type),
                    Accessibility: TsAccessibility.None
                ));
            }

            // Try to find the base constructor argument (the message)
            var syntax = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

            // For primary constructors with base initializer: class Foo(args) : Exception(expr)
            if (syntax is ClassDeclarationSyntax classDecl && classDecl.BaseList is not null)
            {
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
                    {
                        var semanticModel = compilation.GetSemanticModel(primaryBase.SyntaxTree);
                        var exprTransformer = CreateExpressionTransformer(semanticModel);
                        foreach (var arg in primaryBase.ArgumentList.Arguments)
                        {
                            superArgs.Add(exprTransformer.TransformExpression(arg.Expression));
                        }
                    }
                }
            }
        }

        // If we couldn't resolve the super args, just pass all ctor params
        if (superArgs.Count == 0 && ctorParams.Count > 0)
        {
            superArgs.Add(new TsIdentifier(ctorParams[0].Name));
        }

        // Build constructor body: super(message)
        var ctorBody = new List<TsStatement>
        {
            new TsExpressionStatement(
                new TsCallExpression(new TsIdentifier("super"), superArgs)
            )
        };

        var constructor = new TsConstructor(ctorParams, ctorBody);

        // Determine the base class in TS
        TsType extendsType = new TsNamedType("Error");
        if (type.BaseType is not null && IsExceptionType(type.BaseType)
            && type.BaseType.ToDisplayString() != "System.Exception"
            && SymbolHelper.IsTranspilable(type.BaseType, _assemblyWideTranspile, _currentAssembly))
        {
            extendsType = TypeMapper.Map(type.BaseType);
        }

        statements.Add(new TsClass(type.Name, constructor, [], Extends: extendsType));
    }

    private static bool IsExceptionType(INamedTypeSymbol type)
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
            if (SymbolHelper.HasEmit(method)) continue;

            var methodSyntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            if (methodSyntax is null) continue;

            var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            var exprTransformer = CreateExpressionTransformer(semanticModel);
            var body = exprTransformer.TransformBody(methodSyntax.Body, methodSyntax.ExpressionBody,
                isVoid: method.ReturnsVoid);
            var parameters = method.Parameters
                .Select(p => new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
                .ToList();

            var methodName = SymbolHelper.GetNameOverride(method) ?? SymbolHelper.ToCamelCase(method.Name);
            var returnType = TypeMapper.Map(method.ReturnType);
            functions.Add(new TsFunction(methodName, parameters, returnType, body,
                Exported: true, Async: method.IsAsync));
        }

        // export namespace TypeName { ... }
        statements.Add(new TsNamespaceDeclaration(tsTypeName, functions));
        return true;
    }

    private static bool TryGetInlineWrapperPrimitiveType(INamedTypeSymbol type, out TsType primitiveType)
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
    private static bool HasExtensionMembers(INamedTypeSymbol type)
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

        var receiverName = SymbolHelper.ToCamelCase(receiverParamSyntax.Identifier.Text);
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
                        ?? SymbolHelper.ToCamelCase(methodSymbol.Name);
                    var returnType = TypeMapper.Map(methodSymbol.ReturnType);
                    var parameters = new List<TsParameter> { receiverParam };
                    parameters.AddRange(methodSymbol.Parameters.Select(p =>
                        new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type))));

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
                        ?? SymbolHelper.ToCamelCase(propSymbol.Name);
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

        var name = SymbolHelper.GetNameOverride(prop) ?? SymbolHelper.ToCamelCase(prop.Name);
        var returnType = TypeMapper.Map(prop.Type);

        // The receiver parameter
        var parameters = prop.Parameters
            .Select(p => new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
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
        if (SymbolHelper.HasEmit(method)) return null;
        // Skip property accessors — extension properties are handled via their associated property
        if (method.AssociatedSymbol is IPropertySymbol) return null;

        var syntaxNode = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntaxNode is null) return null;

        var name = SymbolHelper.GetNameOverride(method) ?? SymbolHelper.ToCamelCase(method.Name);
        var hasYield = syntaxNode.DescendantNodes().OfType<YieldStatementSyntax>().Any();
        var returnType = hasYield
            ? TypeMapper.MapForGeneratorReturn(method.ReturnType)
            : TypeMapper.Map(method.ReturnType);
        var isAsync = hasYield ? false : method.IsAsync;

        var parameters = method.Parameters
            .Select(p => new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
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
            && SymbolHelper.IsTranspilable(type.BaseType.OriginalDefinition, _assemblyWideTranspile, _currentAssembly))
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
            constructor = GenerateConstructorDispatcher(type, explicitCtors, extendsType);
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
                    && !SymbolHelper.HasEmit(method)
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
                var overloadMembers = GenerateMethodOverloadDispatcher(type, methods);
                classMembers.AddRange(overloadMembers);
            }
        }

        // Generate equals, hashCode, with for records (using ALL params including inherited)
        if (type.IsRecord)
        {
            classMembers.Add(GenerateEquals(type, allParams));
            classMembers.Add(GenerateHashCode(allParams));
            classMembers.Add(GenerateWith(type, allParams));
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

            var name = SymbolHelper.GetNameOverride(member) ?? SymbolHelper.ToCamelCase(member.Name);
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

            var name = SymbolHelper.GetNameOverride(member) ?? SymbolHelper.ToCamelCase(member.Name);
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

        var name = SymbolHelper.GetNameOverride(field) ?? SymbolHelper.ToCamelCase(field.Name);
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
                && capturedParamNames.Contains(SymbolHelper.ToCamelCase(initId.Identifier.Text));

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
        var name = SymbolHelper.GetNameOverride(prop) ?? SymbolHelper.ToCamelCase(prop.Name);
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

        var name = SymbolHelper.GetNameOverride(prop) ?? SymbolHelper.ToCamelCase(prop.Name);
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
        if (SymbolHelper.HasEmit(method))
            return null;
        // Skip compiler-generated or internal/unsupported access levels
        if (method.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable)
            return null;

        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (syntax is null)
            return null;

        var name = SymbolHelper.GetNameOverride(method) ?? SymbolHelper.ToCamelCase(method.Name);
        var hasYield = syntax.DescendantNodes().OfType<YieldStatementSyntax>().Any();
        var returnType = hasYield
            ? TypeMapper.MapForGeneratorReturn(method.ReturnType)
            : TypeMapper.Map(method.ReturnType);
        var isAsync = hasYield ? false : method.IsAsync;

        var parameters = method
            .Parameters.Select(p => new TsParameter(
                SymbolHelper.ToCamelCase(p.Name),
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

    private IReadOnlyList<TsTypeParameter>? ExtractTypeParameters(INamedTypeSymbol type)
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

    private IReadOnlyList<TsTypeParameter>? ExtractMethodTypeParameters(IMethodSymbol method)
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
            var camelName = SymbolHelper.ToCamelCase(param.Name);
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
                return SymbolHelper.ToCamelCase(field.Name);
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

    private static TsAccessibility MapAccessibility(Accessibility accessibility) => accessibility switch
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
                SymbolHelper.ToCamelCase(p.Name),
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

    private static TsMethodMember GenerateEquals(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> ctorParams
    )
    {
        // equals(other: any): boolean {
        //   return other instanceof Type && this.x === other.x && ...
        // }
        TsExpression condition = new TsBinaryExpression(
            new TsIdentifier("other"),
            "instanceof",
            new TsIdentifier(type.Name)
        );

        foreach (var param in ctorParams)
        {
            condition = new TsBinaryExpression(
                condition,
                "&&",
                new TsBinaryExpression(
                    new TsPropertyAccess(new TsIdentifier("this"), param.Name),
                    "===",
                    new TsPropertyAccess(new TsIdentifier("other"), param.Name)
                )
            );
        }

        return new TsMethodMember(
            "equals",
            [new TsParameter("other", new TsAnyType())],
            new TsBooleanType(),
            [new TsReturnStatement(condition)]
        );
    }

    private static TsMethodMember GenerateHashCode(IReadOnlyList<TsConstructorParam> ctorParams)
    {
        // hashCode(): number {
        //   const hc = new HashCode();
        //   hc.add(this.x);
        //   hc.add(this.y);
        //   return hc.toHashCode();
        // }
        var body = new List<TsStatement>();

        body.Add(
            new TsVariableDeclaration("hc", new TsNewExpression(new TsIdentifier("HashCode"), []))
        );

        foreach (var param in ctorParams)
        {
            body.Add(
                new TsExpressionStatement(
                    new TsCallExpression(
                        new TsPropertyAccess(new TsIdentifier("hc"), "add"),
                        [new TsPropertyAccess(new TsIdentifier("this"), param.Name)]
                    )
                )
            );
        }

        body.Add(
            new TsReturnStatement(
                new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "toHashCode"), [])
            )
        );

        return new TsMethodMember("hashCode", [], new TsNumberType(), body);
    }

    private static TsMethodMember GenerateWith(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> ctorParams
    )
    {
        var selfType = MakeSelfType(type);
        var args = ctorParams
            .Select<TsConstructorParam, TsExpression>(p => new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("overrides?"), p.Name),
                "??",
                new TsPropertyAccess(new TsIdentifier("this"), p.Name)
            ))
            .ToList();

        return new TsMethodMember(
            "with",
            [new TsParameter("overrides?", new TsNamedType("Partial", [selfType]))],
            selfType,
            [new TsReturnStatement(new TsNewExpression(new TsIdentifier(type.Name), args))]
        );
    }

    /// <summary>
    /// Creates a TsNamedType for the type including its type parameters.
    /// e.g., Pair with K,V → TsNamedType("Pair", [TsNamedType("K"), TsNamedType("V")])
    /// </summary>
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

    private static TsNamedType MakeSelfType(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
            return new TsNamedType(type.Name);

        var args = type.TypeParameters
            .Select<Microsoft.CodeAnalysis.ITypeParameterSymbol, TsType>(tp => new TsNamedType(tp.Name))
            .ToList();

        return new TsNamedType(type.Name, args);
    }

    // ─── Constructor/Method Overload Dispatch ─────────────

    /// <summary>
    /// Generates a constructor with overload signatures and a dispatcher body.
    /// </summary>
    private TsConstructor GenerateConstructorDispatcher(
        INamedTypeSymbol type,
        List<IMethodSymbol> constructors,
        TsType? extendsType)
    {
        // Sort: most specific first (more params first, then by type specificity)
        var sorted = constructors.OrderByDescending(c => c.Parameters.Length).ToList();

        // Generate overload signatures
        var overloads = sorted.Select(ctor =>
        {
            var @params = ctor.Parameters
                .Select(p => new TsConstructorParam(
                    SymbolHelper.ToCamelCase(p.Name),
                    TypeMapper.Map(p.Type)))
                .ToList();
            return new TsConstructorOverload(@params);
        }).ToList();

        // Generate dispatcher body
        var body = new List<TsStatement>();

        foreach (var ctor in sorted)
        {
            var paramCount = ctor.Parameters.Length;

            // Build condition: args.length === N && typeCheck(args[0]) && ...
            TsExpression condition = new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("args"), "length"),
                "===",
                new TsLiteral(paramCount.ToString()));

            for (var i = 0; i < paramCount; i++)
            {
                var check = GenerateTypeCheckForParam(ctor.Parameters[i].Type, i);
                condition = new TsBinaryExpression(condition, "&&", check);
            }

            // Build assignment body for this overload
            var assignStatements = new List<TsStatement>();

            // If extending, call super — try to resolve from syntax
            if (extendsType is not null)
            {
                var syntax = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (syntax is ConstructorDeclarationSyntax ctorSyntax && ctorSyntax.Initializer is not null
                    && ctorSyntax.Initializer.ThisOrBaseKeyword.Text == "base")
                {
                    var semanticModel = compilation.GetSemanticModel(ctorSyntax.SyntaxTree);
                    var exprTransformer = CreateExpressionTransformer(semanticModel);
                    var superArgs = ctorSyntax.Initializer.ArgumentList.Arguments
                        .Select(a => exprTransformer.TransformExpression(a.Expression))
                        .ToList();
                    assignStatements.Add(new TsExpressionStatement(
                        new TsCallExpression(new TsIdentifier("super"), superArgs)));
                }
            }

            // Transform constructor body
            var ctorSyntaxNode = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (ctorSyntaxNode is ConstructorDeclarationSyntax ctorDecl && ctorDecl.Body is not null)
            {
                var semanticModel = compilation.GetSemanticModel(ctorDecl.SyntaxTree);
                var exprTransformer = CreateExpressionTransformer(semanticModel);

                // Map ctor params to args[i] — replace param names in body
                foreach (var stmt in ctorDecl.Body.Statements)
                {
                    assignStatements.Add(exprTransformer.TransformStatement(stmt));
                }
            }
            else if (ctorSyntaxNode is ConstructorDeclarationSyntax ctorExpr && ctorExpr.ExpressionBody is not null)
            {
                var semanticModel = compilation.GetSemanticModel(ctorExpr.SyntaxTree);
                var exprTransformer = CreateExpressionTransformer(semanticModel);
                assignStatements.Add(new TsExpressionStatement(
                    exprTransformer.TransformExpression(ctorExpr.ExpressionBody.Expression)));
            }

            body.Add(new TsIfStatement(condition, assignStatements));
        }

        // Dispatcher constructor: (...args: unknown[])
        var dispatcherParams = new List<TsConstructorParam>
        {
            new("...args", new TsNamedType("unknown[]"))
        };

        return new TsConstructor(dispatcherParams, body, overloads);
    }

    /// <summary>
    /// Generates a runtime type check for a C# parameter type at a specific args index.
    /// Uses specialized type checks from @meta-sharp/runtime for primitive types.
    /// </summary>
    /// <summary>
    /// Generates a method with overload signatures and a dispatcher body for methods with same name.
    /// </summary>
    private IReadOnlyList<TsClassMember> GenerateMethodOverloadDispatcher(
        INamedTypeSymbol type, List<IMethodSymbol> methods)
    {
        var sorted = methods.OrderByDescending(m => m.Parameters.Length).ToList();
        var firstName = sorted[0];
        var name = SymbolHelper.GetNameOverride(firstName) ?? SymbolHelper.ToCamelCase(firstName.Name);
        var isStatic = firstName.IsStatic;
        var isAsync = sorted.Any(m => m.IsAsync);
        var accessibility = MapAccessibility(firstName.DeclaredAccessibility);

        // Determine a common return type (use unknown if they differ)
        var returnTypes = sorted.Select(m => TypeMapper.Map(m.ReturnType)).ToList();
        TsType commonReturn;
        if (returnTypes.All(t => t == returnTypes[0]))
        {
            commonReturn = returnTypes[0];
        }
        else if (isAsync)
        {
            commonReturn = new TsNamedType("Promise", [new TsNamedType("unknown")]);
        }
        else
        {
            commonReturn = new TsAnyType();
        }

        // Generate overload signatures (kept on the dispatcher for backward compat)
        var overloads = sorted.Select(m =>
        {
            var @params = m.Parameters
                .Select(p => new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
                .ToList();
            return new TsMethodOverload(@params, TypeMapper.Map(m.ReturnType));
        }).ToList();

        // Compute fast-path names (one per overload, unique within the group)
        var fastPathNames = ComputeFastPathNames(name, sorted);

        // Generate fast-path methods (specialized, one per overload, with the real body)
        var members = new List<TsClassMember>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var method = sorted[i];
            var fastPathName = fastPathNames[i];
            var fastPathMethod = GenerateFastPathMethod(method, fastPathName, isStatic, accessibility);
            if (fastPathMethod is not null) members.Add(fastPathMethod);
        }

        // Generate dispatcher body that delegates to fast-paths
        var body = new List<TsStatement>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var method = sorted[i];
            var fastPathName = fastPathNames[i];
            var paramCount = method.Parameters.Length;

            // Build condition: args.length === N && typeCheck(args[0]) && ...
            TsExpression condition = new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("args"), "length"),
                "===",
                new TsLiteral(paramCount.ToString()));

            for (var j = 0; j < paramCount; j++)
            {
                var check = GenerateTypeCheckForParam(method.Parameters[j].Type, j);
                condition = new TsBinaryExpression(condition, "&&", check);
            }

            // Build delegating call: this.fastPathName(args[0] as T0, args[1] as T1, ...)
            // or for static: ClassName.fastPathName(args[0] as T0, ...)
            var callArgs = new List<TsExpression>();
            for (var j = 0; j < paramCount; j++)
            {
                var paramType = TypeMapper.Map(method.Parameters[j].Type);
                callArgs.Add(new TsCastExpression(
                    new TsIdentifier($"args[{j}]"),
                    paramType));
            }

            var receiver = isStatic
                ? (TsExpression)new TsIdentifier(type.Name)
                : new TsIdentifier("this");
            var delegateCall = new TsCallExpression(
                new TsPropertyAccess(receiver, fastPathName),
                callArgs);

            var branchStatements = new List<TsStatement>();
            if (method.ReturnsVoid)
            {
                branchStatements.Add(new TsExpressionStatement(delegateCall));
                branchStatements.Add(new TsReturnStatement());
            }
            else
            {
                branchStatements.Add(new TsReturnStatement(delegateCall));
            }

            body.Add(new TsIfStatement(condition, branchStatements));
        }

        // Add throw at the end for unmatched overloads
        body.Add(new TsThrowStatement(
            new TsNewExpression(new TsIdentifier("Error"),
                [new TsStringLiteral($"No matching overload for {name}")])));

        // Dispatcher params: ...args: unknown[]
        var dispatcherParams = new List<TsParameter>
        {
            new("...args", new TsNamedType("unknown[]"))
        };

        members.Add(new TsMethodMember(name, dispatcherParams, commonReturn, body,
            Static: isStatic, Async: isAsync, Accessibility: accessibility, Overloads: overloads));

        return members;
    }

    /// <summary>
    /// Generates a specialized method for one specific overload (fast path).
    /// Has the real body (not wrapped in a runtime check).
    /// </summary>
    private TsMethodMember? GenerateFastPathMethod(
        IMethodSymbol method, string fastPathName, bool isStatic, TsAccessibility accessibility)
    {
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        if (syntax is null) return null;

        var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
        var exprTransformer = CreateExpressionTransformer(semanticModel);
        if (!method.IsStatic) exprTransformer.SelfParameterName = "this";

        var parameters = method.Parameters
            .Select(p => new TsParameter(SymbolHelper.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
            .ToList();
        var returnType = TypeMapper.Map(method.ReturnType);
        var body = exprTransformer.TransformBody(syntax.Body, syntax.ExpressionBody, isVoid: method.ReturnsVoid);

        return new TsMethodMember(
            fastPathName,
            parameters,
            returnType,
            body,
            Static: isStatic,
            Async: method.IsAsync,
            Accessibility: TsAccessibility.Private,  // fast paths are internal — dispatcher is the public API
            TypeParameters: ExtractMethodTypeParameters(method)
        );
    }

    /// <summary>
    /// Computes a unique fast-path name for each overload in a group.
    /// Strategy: name + capitalized param names (e.g., addXY). On conflict, append type names.
    /// </summary>
    private static IReadOnlyList<string> ComputeFastPathNames(string baseName, IReadOnlyList<IMethodSymbol> methods)
    {
        var firstAttempt = methods
            .Select(m => baseName + string.Concat(m.Parameters.Select(p => Capitalize(p.Name))))
            .ToList();

        if (firstAttempt.Distinct().Count() == firstAttempt.Count)
            return firstAttempt;

        // Conflict — fall back to type-based naming
        return methods.Select(m =>
        {
            var typeSuffix = string.Concat(m.Parameters.Select(p =>
                Capitalize(SimpleTypeName(p.Type))));
            return baseName + typeSuffix;
        }).ToList();
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string SimpleTypeName(ITypeSymbol type)
    {
        var name = type.Name;
        // Use a clean primitive alias
        return type.SpecialType switch
        {
            SpecialType.System_Int32 => "Int",
            SpecialType.System_Int64 => "Long",
            SpecialType.System_String => "String",
            SpecialType.System_Boolean => "Bool",
            SpecialType.System_Double => "Double",
            SpecialType.System_Single => "Float",
            _ => name,
        };
    }

    /// <summary>
    /// Generates an exhaustive check for StringEnum values: (v === "a" || v === "b" || v === "c")
    /// </summary>
    private static TsExpression GenerateInlineWrapperCheck(TsType primitiveType, TsExpression argAccess)
    {
        var jsType = primitiveType switch
        {
            TsStringType => "string",
            TsNumberType => "number",
            TsBooleanType => "boolean",
            TsBigIntType => "bigint",
            _ => "object",
        };
        return new TsBinaryExpression(
            new TsUnaryExpression("typeof ", argAccess),
            "===",
            new TsStringLiteral(jsType));
    }

    private static TsExpression GenerateStringEnumCheck(INamedTypeSymbol enumType, TsExpression argAccess)
    {
        var members = enumType.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .ToList();

        if (members.Count == 0)
            return new TsCallExpression(new TsIdentifier("isString"), [argAccess]);

        TsExpression check = members
            .Select<IFieldSymbol, TsExpression>(m =>
            {
                var name = SymbolHelper.GetNameOverride(m) ?? m.Name;
                return new TsBinaryExpression(argAccess, "===", new TsStringLiteral(name));
            })
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

        return new TsParenthesized(check);
    }

    private TsExpression GenerateTypeCheckForParam(ITypeSymbol csharpType, int argIndex)
    {
        var argAccess = new TsIdentifier($"args[{argIndex}]");

        var fullName = csharpType.ToDisplayString();

        // Specialized runtime type checks for numeric types
        return fullName switch
        {
            "char" => new TsCallExpression(new TsIdentifier("isChar"), [argAccess]),
            "string" => new TsCallExpression(new TsIdentifier("isString"), [argAccess]),
            "byte" => new TsCallExpression(new TsIdentifier("isByte"), [argAccess]),
            "sbyte" => new TsCallExpression(new TsIdentifier("isSByte"), [argAccess]),
            "short" or "System.Int16" => new TsCallExpression(new TsIdentifier("isInt16"), [argAccess]),
            "ushort" or "System.UInt16" => new TsCallExpression(new TsIdentifier("isUInt16"), [argAccess]),
            "int" or "System.Int32" => new TsCallExpression(new TsIdentifier("isInt32"), [argAccess]),
            "uint" or "System.UInt32" => new TsCallExpression(new TsIdentifier("isUInt32"), [argAccess]),
            "long" or "System.Int64" => new TsCallExpression(new TsIdentifier("isInt64"), [argAccess]),
            "ulong" or "System.UInt64" => new TsCallExpression(new TsIdentifier("isUInt64"), [argAccess]),
            "float" or "System.Single" => new TsCallExpression(new TsIdentifier("isFloat32"), [argAccess]),
            "double" or "System.Double" => new TsCallExpression(new TsIdentifier("isFloat64"), [argAccess]),
            "bool" or "System.Boolean" => new TsCallExpression(new TsIdentifier("isBool"), [argAccess]),
            "decimal" or "System.Decimal" => new TsCallExpression(new TsIdentifier("isFloat64"), [argAccess]),
            "System.Numerics.BigInteger" => new TsCallExpression(new TsIdentifier("isBigInt"), [argAccess]),

            // Enums with [StringEnum] → exhaustive value check
            _ when csharpType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                && SymbolHelper.HasStringEnum(enumType) =>
                GenerateStringEnumCheck(enumType, argAccess),

            // Numeric enums → typeof number
            _ when csharpType is INamedTypeSymbol { TypeKind: TypeKind.Enum } =>
                new TsCallExpression(new TsIdentifier("isInt32"), [argAccess]),

            // Interfaces → shape check (typeof object, can't use instanceof)
            _ when csharpType is INamedTypeSymbol { TypeKind: TypeKind.Interface } =>
                new TsBinaryExpression(
                    new TsUnaryExpression("typeof ", argAccess),
                    "===", new TsStringLiteral("object")),

            // InlineWrapper structs → typeof check on the underlying primitive
            _ when csharpType is INamedTypeSymbol inlineType
                && SymbolHelper.HasInlineWrapper(inlineType)
                && TryGetInlineWrapperPrimitiveType(inlineType, out var primType) =>
                GenerateInlineWrapperCheck(primType, argAccess),

            // Classes/records → instanceof
            _ when csharpType is INamedTypeSymbol named
                && SymbolHelper.IsTranspilable(named, _assemblyWideTranspile, _currentAssembly) =>
                new TsBinaryExpression(argAccess, "instanceof", new TsIdentifier(named.Name)),

            // Arrays
            _ when csharpType is IArrayTypeSymbol =>
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                    [argAccess]),

            // Default: typeof check based on TS type
            _ => new TsBinaryExpression(
                new TsUnaryExpression("typeof ", argAccess),
                "===",
                new TsStringLiteral("object"))
        };
    }

    // ─── Type Guards ────────────────────────────────────────

    /// <summary>
    /// Generates a type guard function for a transpiled type.
    /// Returns null for types that don't need guards (e.g., ExportedAsModule).
    /// </summary>
    private TsFunction? GenerateTypeGuard(INamedTypeSymbol type)
    {
        // Skip types that don't need guards
        if (IsExceptionType(type)) return null;
        if (SymbolHelper.HasExportedAsModule(type) || HasExtensionMembers(type)) return null;
        if (SymbolHelper.HasImport(type)) return null;

        var tsName = GetTsTypeName(type);
        var guardName = $"is{tsName}";
        var valueParam = new TsParameter("value", new TsNamedType("unknown"));
        var returnType = new TsNamedType(tsName); // TS "value is TypeName" predicate

        if (type.TypeKind == TypeKind.Enum)
            return GenerateEnumGuard(type, guardName, tsName, valueParam);

        if (type.TypeKind == TypeKind.Interface)
            return GenerateShapeGuard(type, guardName, tsName, valueParam, useInstanceof: false);

        if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
            return GenerateShapeGuard(type, guardName, tsName, valueParam, useInstanceof: true);

        return null;
    }

    private TsFunction GenerateEnumGuard(
        INamedTypeSymbol type, string guardName, string tsName, TsParameter valueParam)
    {
        var isStringEnum = SymbolHelper.HasStringEnum(type);
        var members = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();

        TsExpression condition;

        if (isStringEnum)
        {
            // value === "BRL" || value === "USD" || ...
            condition = members
                .Select<IFieldSymbol, TsExpression>(m =>
                {
                    var name = SymbolHelper.GetNameOverride(m) ?? m.Name;
                    return new TsBinaryExpression(
                        new TsIdentifier("value"), "===", new TsStringLiteral(name));
                })
                .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));
        }
        else
        {
            // typeof value === "number" && (value === 0 || value === 1 || ...)
            var valueChecks = members
                .Select<IFieldSymbol, TsExpression>(m =>
                    new TsBinaryExpression(
                        new TsIdentifier("value"), "===", new TsLiteral(m.ConstantValue!.ToString()!)))
                .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

            condition = new TsBinaryExpression(
                new TsBinaryExpression(
                    new TsUnaryExpression("typeof ", new TsIdentifier("value")),
                    "===", new TsStringLiteral("number")),
                "&&",
                new TsParenthesized(valueChecks));
        }

        // The return type annotation "value is TypeName" is expressed as a named type
        // that the Printer will handle via a special TypePredicate representation.
        // For now, we use TsNamedType and handle it in the function signature.
        return new TsFunction(guardName, [valueParam],
            new TsTypePredicateType("value", new TsNamedType(tsName)),
            [new TsReturnStatement(condition)]);
    }

    private TsFunction GenerateShapeGuard(
        INamedTypeSymbol type, string guardName, string tsName, TsParameter valueParam,
        bool useInstanceof)
    {
        var body = new List<TsStatement>();

        // instanceof fast path (for classes/records only)
        if (useInstanceof)
        {
            body.Add(new TsIfStatement(
                new TsBinaryExpression(new TsIdentifier("value"), "instanceof", new TsIdentifier(tsName)),
                [new TsReturnStatement(new TsLiteral("true"))]
            ));
        }

        // Null/object check
        body.Add(new TsIfStatement(
            new TsBinaryExpression(
                new TsBinaryExpression(new TsIdentifier("value"), "==", new TsLiteral("null")),
                "||",
                new TsBinaryExpression(
                    new TsUnaryExpression("typeof ", new TsIdentifier("value")),
                    "!==", new TsStringLiteral("object"))),
            [new TsReturnStatement(new TsLiteral("false"))]
        ));

        // const v = value as any;
        body.Add(new TsVariableDeclaration("v",
            new TsCastExpression(new TsIdentifier("value"), new TsAnyType())));

        // Field checks
        var fields = GetAllFieldsForGuard(type);
        if (fields.Count > 0)
        {
            TsExpression fieldChecks = fields
                .Select(f => GenerateFieldCheck(new TsPropertyAccess(new TsIdentifier("v"), f.Name), f.Type))
                .Aggregate((a, b) => new TsBinaryExpression(a, "&&", b));

            body.Add(new TsReturnStatement(fieldChecks));
        }
        else
        {
            body.Add(new TsReturnStatement(new TsLiteral("true")));
        }

        return new TsFunction(guardName, [valueParam],
            new TsTypePredicateType("value", new TsNamedType(tsName)), body);
    }

    /// <summary>
    /// Gets all fields (own + inherited) for guard validation.
    /// </summary>
    private IReadOnlyList<(string Name, TsType Type)> GetAllFieldsForGuard(INamedTypeSymbol type)
    {
        var fields = new List<(string Name, TsType Type)>();

        // Collect from all levels of hierarchy
        var current = type;
        while (current is not null && current.SpecialType == SpecialType.None
            && current.ToDisplayString() is not "System.Object" and not "System.ValueType")
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsImplicitlyDeclared) continue;
                if (member.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) continue;
                if (SymbolHelper.HasIgnore(member)) continue;

                var name = SymbolHelper.GetNameOverride(member) ?? SymbolHelper.ToCamelCase(member.Name);
                var tsType = TypeMapper.Map(member.Type);

                // Avoid duplicates (from overrides)
                if (fields.All(f => f.Name != name))
                    fields.Add((name, tsType));
            }

            current = current.BaseType;
        }

        return fields;
    }

    /// <summary>
    /// Generates a runtime type check expression for a single field.
    /// </summary>
    private TsExpression GenerateFieldCheck(TsExpression fieldAccess, TsType fieldType)
    {
        return fieldType switch
        {
            TsNumberType => TypeofCheck(fieldAccess, "number"),
            TsStringType => TypeofCheck(fieldAccess, "string"),
            TsBooleanType => TypeofCheck(fieldAccess, "boolean"),
            TsBigIntType => TypeofCheck(fieldAccess, "bigint"),

            TsArrayType => new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                [fieldAccess]),

            TsNamedType { Name: "Map" } => new TsBinaryExpression(
                fieldAccess, "instanceof", new TsIdentifier("Map")),

            TsNamedType { Name: "Set" } => new TsBinaryExpression(
                fieldAccess, "instanceof", new TsIdentifier("Set")),

            // Temporal types
            TsNamedType { Name: var n } when n.StartsWith("Temporal.") =>
                new TsBinaryExpression(fieldAccess, "instanceof", new TsIdentifier(n)),

            // Transpilable named type → call guard recursively
            TsNamedType { Name: var n } when _transpilableTypeMap.ContainsKey(n) =>
                new TsCallExpression(new TsIdentifier($"is{n}"), [fieldAccess]),

            // Union with null (nullable) → field == null || innerCheck
            TsUnionType { Types: var types } when types.Any(t => t is TsNamedType { Name: "null" }) =>
                NullableFieldCheck(fieldAccess, types),

            // String literal union (from StringEnum that's not transpilable)
            TsUnionType { Types: var types } when types.All(t => t is TsStringLiteralType) =>
                types.Cast<TsStringLiteralType>()
                    .Select<TsStringLiteralType, TsExpression>(t =>
                        new TsBinaryExpression(fieldAccess, "===", new TsStringLiteral(t.Value)))
                    .Aggregate((a, b) => new TsBinaryExpression(a, "||", b)),

            TsTupleType { Elements: var elements } =>
                new TsBinaryExpression(
                    new TsCallExpression(
                        new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                        [fieldAccess]),
                    "&&",
                    new TsBinaryExpression(
                        new TsPropertyAccess(fieldAccess, "length"),
                        "===",
                        new TsLiteral(elements.Count.ToString()))),

            TsAnyType or TsVoidType or TsPromiseType => new TsLiteral("true"),

            // Unknown type — accept anything
            _ => new TsLiteral("true"),
        };
    }

    private static TsExpression TypeofCheck(TsExpression expr, string typeName) =>
        new TsBinaryExpression(
            new TsUnaryExpression("typeof ", expr),
            "===",
            new TsStringLiteral(typeName));

    private TsExpression NullableFieldCheck(TsExpression fieldAccess, IReadOnlyList<TsType> unionTypes)
    {
        var nonNullTypes = unionTypes.Where(t => t is not TsNamedType { Name: "null" }).ToList();
        if (nonNullTypes.Count == 0) return new TsLiteral("true");

        var innerCheck = nonNullTypes.Count == 1
            ? GenerateFieldCheck(fieldAccess, nonNullTypes[0])
            : nonNullTypes
                .Select(t => GenerateFieldCheck(fieldAccess, t))
                .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

        return new TsBinaryExpression(
            new TsBinaryExpression(fieldAccess, "==", new TsLiteral("null")),
            "||",
            innerCheck);
    }

    // ─── Imports ────────────────────────────────────────────

    private IReadOnlyList<TsImport> CollectImports(
        INamedTypeSymbol currentType,
        List<TsTopLevel> statements
    )
    {
        var referencedTypes = new HashSet<string>();
        var valueTypes = new HashSet<string>(); // types used via `new` or `extends` (need runtime import)
        CollectReferencedTypeNames(statements, referencedTypes, valueTypes);

        var tsTypeName = GetTsTypeName(currentType);
        referencedTypes.Remove(currentType.Name);
        referencedTypes.Remove(tsTypeName);
        referencedTypes.Remove($"is{tsTypeName}"); // own guard — don't import

        var imports = new List<TsImport>();
        var currentNs = GetNamespace(currentType);

        // Runtime imports (HashCode for records)
        if (currentType.IsRecord)
        {
            imports.Add(new TsImport(["HashCode"], "@meta-sharp/runtime"));
        }

        // Temporal polyfill import (if any Temporal types are referenced)
        if (referencedTypes.Any(t => t.StartsWith("Temporal.")))
        {
            imports.Add(new TsImport(["Temporal"], "@js-temporal/polyfill"));
        }

        // Runtime type check imports (isString, isInt32, etc.)
        var runtimeTypeChecks = referencedTypes
            .Where(IsRuntimeTypeCheck)
            .OrderBy(n => n)
            .ToArray();

        if (runtimeTypeChecks.Length > 0)
        {
            imports.Add(new TsImport(runtimeTypeChecks, "@meta-sharp/runtime"));
        }

        // LINQ Enumerable import
        if (referencedTypes.Contains("Enumerable"))
        {
            imports.Add(new TsImport(["Enumerable"], "@meta-sharp/runtime"));
        }

        // Runtime helper imports (dayNumber, etc.)
        if (referencedTypes.Contains("dayNumber"))
        {
            imports.Add(new TsImport(["dayNumber"], "@meta-sharp/runtime"));
        }

        // HashSet import (from runtime collections)
        if (referencedTypes.Contains("HashSet"))
        {
            imports.Add(new TsImport(["HashSet"], "@meta-sharp/runtime"));
        }

        // LINQ Grouping type import
        if (referencedTypes.Contains("Grouping"))
        {
            imports.Add(new TsImport(["Grouping"], "@meta-sharp/runtime", TypeOnly: true));
        }

        // Track what we've already imported to avoid duplicates
        var importedNames = new HashSet<string>(runtimeTypeChecks) { "Enumerable", "Grouping", "HashSet", "dayNumber" };

        foreach (var typeName in referencedTypes.OrderBy(n => n))
        {
            // Skip built-in types and runtime identifiers that don't need imports
            if (typeName.StartsWith("Temporal.") || IsRuntimeTypeCheck(typeName)
                || typeName is "Map" or "Set"
                or "unknown" or "any" or "null" or "Partial" or "Error" or "HashCode"
                or "Array" or "v" or "value" or "true" or "false" or "undefined"
                or "console" or "Math" or "crypto" or "Object" or "typeof"
                or "unknown[]")
                continue;

            // BCL export mapping (e.g., decimal → Decimal from "decimal.js")
            var bclEntry = _bclExportMap.Values.FirstOrDefault(e => e.ExportedName == typeName);
            if (bclEntry.ExportedName is not null && bclEntry.FromPackage.Length > 0
                && importedNames.Add(typeName))
            {
                imports.Add(new TsImport([bclEntry.ExportedName], bclEntry.FromPackage));
                continue;
            }

            // External import mapping ([Import] attribute)
            if (_externalImportMap.TryGetValue(typeName, out var extImport)
                && importedNames.Add(typeName))
            {
                imports.Add(new TsImport([extImport.Name], extImport.From));
                continue;
            }

            // Guard function reference (e.g., isCurrency → import from Currency's file)
            if (_guardNameToTypeMap.TryGetValue(typeName, out var guardedTypeName)
                && _transpilableTypeMap.TryGetValue(guardedTypeName, out var guardedSymbol)
                && importedNames.Add(typeName))
            {
                var guardNs = GetNamespace(guardedSymbol);
                var guardTsName = GetTsTypeName(guardedSymbol);
                var guardPath = ComputeRelativeImportPath(currentNs, guardNs, guardTsName);
                imports.Add(new TsImport([typeName], guardPath));
                continue;
            }

            // Transpilable type within the project
            if (!_transpilableTypeMap.TryGetValue(typeName, out var referencedSymbol))
                continue;

            if (!importedNames.Add(typeName)) continue;

            var targetNs = GetNamespace(referencedSymbol);
            var targetTsName = GetTsTypeName(referencedSymbol);
            var importPath = ComputeRelativeImportPath(currentNs, targetNs, targetTsName);
            // StringEnums generate const objects — always import as value
            var isStringEnum = SymbolHelper.HasStringEnum(referencedSymbol);
            var typeOnly = !valueTypes.Contains(typeName) && !isStringEnum;
            imports.Add(new TsImport([targetTsName], importPath, TypeOnly: typeOnly));
        }

        return imports;
    }

    private static void CollectReferencedTypeNames(
        IEnumerable<TsTopLevel> statements,
        HashSet<string> names,
        HashSet<string> valueNames
    )
    {
        foreach (var stmt in statements)
        {
            CollectFromTopLevel(stmt, names, valueNames);
        }
    }

    private static void CollectFromTopLevel(TsTopLevel node, HashSet<string> names, HashSet<string> valueNames)
    {
        switch (node)
        {
            case TsInterface iface:
                CollectFromTypeParameters(iface.TypeParameters, names);
                foreach (var prop in iface.Properties)
                    CollectFromType(prop.Type, names);
                if (iface.Methods is not null)
                    foreach (var method in iface.Methods)
                    {
                        CollectFromTypeParameters(method.TypeParameters, names);
                        foreach (var p in method.Parameters)
                            CollectFromType(p.Type, names);
                        CollectFromType(method.ReturnType, names);
                    }
                break;
            case TsFunction func:
                CollectFromTypeParameters(func.TypeParameters, names);
                foreach (var param in func.Parameters)
                    CollectFromType(param.Type, names);
                CollectFromType(func.ReturnType, names);
                CollectFromStatements(func.Body, names, valueNames);
                break;
            case TsConstObject constObj:
                foreach (var (_, value) in constObj.Entries)
                    CollectFromExpression(value, names, valueNames);
                break;
            case TsNamespaceDeclaration ns:
                foreach (var func in ns.Functions)
                {
                    foreach (var p in func.Parameters)
                        CollectFromType(p.Type, names);
                    CollectFromType(func.ReturnType, names);
                    CollectFromStatements(func.Body, names, valueNames);
                }
                break;
            case TsClass cls:
                CollectFromTypeParameters(cls.TypeParameters, names);
                if (cls.Extends is not null)
                {
                    CollectFromType(cls.Extends, names);
                    if (cls.Extends is TsNamedType extendsNamed)
                        valueNames.Add(extendsNamed.Name);
                }
                if (cls.Implements is not null)
                    foreach (var iface in cls.Implements)
                        CollectFromType(iface, names);
                if (cls.Constructor is not null)
                {
                    foreach (var p in cls.Constructor.Parameters)
                        CollectFromType(p.Type, names);
                    CollectFromStatements(cls.Constructor.Body, names, valueNames);
                }
                foreach (var member in cls.Members)
                {
                    switch (member)
                    {
                        case TsMethodMember m:
                            CollectFromTypeParameters(m.TypeParameters, names);
                            foreach (var p in m.Parameters)
                                CollectFromType(p.Type, names);
                            CollectFromType(m.ReturnType, names);
                            CollectFromStatements(m.Body, names, valueNames);
                            // Collect from overload signatures too
                            if (m.Overloads is not null)
                                foreach (var overload in m.Overloads)
                                {
                                    foreach (var p in overload.Parameters)
                                        CollectFromType(p.Type, names);
                                    CollectFromType(overload.ReturnType, names);
                                }
                            break;
                        case TsGetterMember g:
                            CollectFromType(g.ReturnType, names);
                            CollectFromStatements(g.Body, names, valueNames);
                            break;
                        case TsFieldMember f:
                            CollectFromType(f.Type, names);
                            if (f.Initializer is not null)
                                CollectFromExpression(f.Initializer, names, valueNames);
                            break;
                    }
                }
                break;
        }
    }

    private static void CollectFromTypeParameters(IReadOnlyList<TsTypeParameter>? typeParams, HashSet<string> names)
    {
        if (typeParams is null) return;
        foreach (var tp in typeParams)
        {
            if (tp.Constraint is not null)
                CollectFromType(tp.Constraint, names);
        }
    }

    private static void CollectFromType(TsType type, HashSet<string> names)
    {
        switch (type)
        {
            case TsNamedType named:
                names.Add(named.Name);
                // For nested types like "Outer.Inner", also add the root name "Outer"
                // (the import is for the outer type, accessed via declaration merging).
                if (named.Name.Contains('.'))
                {
                    var rootName = named.Name[..named.Name.IndexOf('.')];
                    names.Add(rootName);
                }
                if (named.TypeArguments is not null)
                    foreach (var arg in named.TypeArguments)
                        CollectFromType(arg, names);
                break;
            case TsArrayType array:
                CollectFromType(array.ElementType, names);
                break;
            case TsPromiseType promise:
                CollectFromType(promise.Inner, names);
                break;
            case TsUnionType union:
                foreach (var t in union.Types)
                    CollectFromType(t, names);
                break;
            case TsIntersectionType intersection:
                foreach (var t in intersection.Types)
                    CollectFromType(t, names);
                break;
        }
    }

    private static void CollectFromStatements(IReadOnlyList<TsStatement> statements, HashSet<string> names, HashSet<string> valueNames)
    {
        foreach (var stmt in statements)
            CollectFromStatement(stmt, names, valueNames);
    }

    private static void CollectFromStatement(TsStatement stmt, HashSet<string> names, HashSet<string> valueNames)
    {
        switch (stmt)
        {
            case TsReturnStatement ret:
                if (ret.Expression is not null) CollectFromExpression(ret.Expression, names, valueNames);
                break;
            case TsThrowStatement thr:
                CollectFromExpression(thr.Expression, names, valueNames);
                break;
            case TsExpressionStatement expr:
                CollectFromExpression(expr.Expression, names, valueNames);
                break;
            case TsIfStatement ifStmt:
                CollectFromExpression(ifStmt.Condition, names, valueNames);
                CollectFromStatements(ifStmt.Then, names, valueNames);
                if (ifStmt.Else is not null) CollectFromStatements(ifStmt.Else, names, valueNames);
                break;
            case TsVariableDeclaration varDecl:
                CollectFromExpression(varDecl.Initializer, names, valueNames);
                break;
        }
    }

    private static void CollectFromExpression(TsExpression expr, HashSet<string> names, HashSet<string> valueNames)
    {
        switch (expr)
        {
            case TsNewExpression newExpr:
                if (newExpr.Callee is TsIdentifier id)
                {
                    names.Add(id.Name);
                    valueNames.Add(id.Name); // used as value (constructor call)
                    // For nested types like "Outer.Inner", also mark the root as value
                    if (id.Name.Contains('.'))
                    {
                        var rootName = id.Name[..id.Name.IndexOf('.')];
                        names.Add(rootName);
                        valueNames.Add(rootName);
                    }
                }
                foreach (var arg in newExpr.Arguments)
                    CollectFromExpression(arg, names, valueNames);
                break;
            case TsCallExpression call:
                // Function calls may reference guard functions (e.g., isCurrency)
                if (call.Callee is TsIdentifier callId)
                {
                    names.Add(callId.Name);
                    valueNames.Add(callId.Name);
                }
                // Static method calls like Enumerable.from(...)
                else if (call.Callee is TsPropertyAccess { Object: TsIdentifier { Name: var rootName } }
                    && char.IsUpper(rootName[0]))
                {
                    names.Add(rootName);
                    valueNames.Add(rootName);
                    CollectFromExpression(call.Callee, names, valueNames);
                }
                else
                {
                    CollectFromExpression(call.Callee, names, valueNames);
                }
                foreach (var arg in call.Arguments)
                    CollectFromExpression(arg, names, valueNames);
                break;
            case TsPropertyAccess access:
                CollectFromExpression(access.Object, names, valueNames);
                // Static member access like IssuePriority.High → collect the type as value
                if (access.Object is TsIdentifier { Name: var propObjName }
                    && propObjName.Length > 0 && char.IsUpper(propObjName[0]))
                {
                    names.Add(propObjName);
                    valueNames.Add(propObjName);
                }
                break;
            case TsBinaryExpression bin:
                CollectFromExpression(bin.Left, names, valueNames);
                CollectFromExpression(bin.Right, names, valueNames);
                // instanceof uses the type as a value
                if (bin.Operator == "instanceof" && bin.Right is TsIdentifier instanceId)
                    valueNames.Add(instanceId.Name);
                break;
            case TsObjectLiteral obj:
                foreach (var prop in obj.Properties)
                    CollectFromExpression(prop.Value, names, valueNames);
                break;
            case TsTemplateLiteral tmpl:
                foreach (var e in tmpl.Expressions)
                    CollectFromExpression(e, names, valueNames);
                break;
            case TsConditionalExpression cond:
                CollectFromExpression(cond.Condition, names, valueNames);
                CollectFromExpression(cond.WhenTrue, names, valueNames);
                CollectFromExpression(cond.WhenFalse, names, valueNames);
                break;
            case TsAwaitExpression await_:
                CollectFromExpression(await_.Expression, names, valueNames);
                break;
            case TsParenthesized paren:
                CollectFromExpression(paren.Expression, names, valueNames);
                break;
            case TsSpreadExpression spread:
                CollectFromExpression(spread.Expression, names, valueNames);
                break;
            case TsArrowFunction arrow:
                foreach (var p in arrow.Parameters)
                    CollectFromType(p.Type, names);
                CollectFromStatements(arrow.Body, names, valueNames);
                break;
            case TsElementAccess elemAccess:
                CollectFromExpression(elemAccess.Object, names, valueNames);
                CollectFromExpression(elemAccess.Index, names, valueNames);
                break;
            case TsArrayLiteral arrayLit:
                foreach (var e in arrayLit.Elements)
                    CollectFromExpression(e, names, valueNames);
                break;
        }
    }

    // ─── Namespace / path helpers ───────────────────────────

    /// <summary>
    /// Checks if a name is a runtime type check function from @meta-sharp/runtime.
    /// </summary>
    private static bool IsRuntimeTypeCheck(string name) => name is
        "isChar" or "isString" or "isByte" or "isSByte"
        or "isInt16" or "isUInt16" or "isInt32" or "isUInt32"
        or "isInt64" or "isUInt64" or "isFloat32" or "isFloat64"
        or "isBool" or "isBigInt";

    private ExpressionTransformer CreateExpressionTransformer(SemanticModel semanticModel) =>
        new(semanticModel)
        {
            AssemblyWideTranspile = _assemblyWideTranspile,
            CurrentAssembly = _currentAssembly,
            ReportDiagnostic = _diagnostics.Add,
        };

    private static string GetNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        return ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
    }

    /// <summary>
    /// Strips the root namespace and converts remaining segments to a file path.
    /// e.g., root="Orzano.Shared", ns="Orzano.Shared.Models", name="Money" → "Models/Money.ts"
    /// </summary>
    private string GetRelativePath(string ns, string typeName)
    {
        var relative = StripRootNamespace(ns);
        var segments = relative.Length > 0
            ? relative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
            : [];

        var fileName = SymbolHelper.ToKebabCase(typeName);
        var path = segments.Length > 0
            ? string.Join("/", segments) + "/" + fileName + ".ts"
            : fileName + ".ts";

        return path;
    }

    private string StripRootNamespace(string ns)
    {
        if (_rootNamespace.Length == 0 || ns.Length == 0) return ns;
        if (ns == _rootNamespace) return "";
        if (ns.StartsWith(_rootNamespace + "."))
            return ns[(_rootNamespace.Length + 1)..];
        return ns;
    }

    /// <summary>
    /// Computes a relative import path from one namespace to another.
    /// e.g., from "Orzano.Shared" to "Orzano.Shared.Models" for type "Foo" → "./Models/Foo"
    /// </summary>
    /// <summary>
    /// Computes the absolute import path for a type, using the `#/` subpath import alias.
    /// All cross-file imports use `#/<namespace-path>/<type-name>` format. The consumer
    /// configures `package.json#imports` and `tsconfig#paths` to resolve `#/*` to `./src/*`.
    /// </summary>
    private string ComputeRelativeImportPath(string fromNs, string toNs, string typeName)
    {
        // fromNs is unused — we always emit absolute paths from #/ root
        var toRelative = StripRootNamespace(toNs);
        var toParts = toRelative.Length > 0
            ? toRelative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
            : [];

        var parts = new List<string> { "#" };
        parts.AddRange(toParts);
        parts.Add(SymbolHelper.ToKebabCase(typeName));
        return string.Join("/", parts);
    }

    /// <summary>
    /// Finds the longest common dot-separated namespace prefix.
    /// </summary>
    private static string FindCommonNamespacePrefix(IReadOnlyList<string> namespaces)
    {
        if (namespaces.Count == 0) return "";
        if (namespaces.Count == 1) return namespaces[0];

        var parts = namespaces[0].Split('.');
        var commonLength = parts.Length;

        for (var i = 1; i < namespaces.Count; i++)
        {
            var otherParts = namespaces[i].Split('.');
            commonLength = Math.Min(commonLength, otherParts.Length);

            for (var j = 0; j < commonLength; j++)
            {
                if (parts[j] != otherParts[j])
                {
                    commonLength = j;
                    break;
                }
            }
        }

        return string.Join(".", parts.Take(commonLength));
    }

    /// <summary>
    /// Generates leaf-only index.ts barrel files for each namespace directory that contains
    /// type files. Parent aggregator barrels (re-exporting subdirectories) are NOT generated —
    /// consumers must use full paths to access nested namespaces.
    /// </summary>
    private IReadOnlyList<TsSourceFile> GenerateIndexFiles(IReadOnlyList<TsSourceFile> typeFiles)
    {
        // Group files by their directory
        var dirToFiles = new Dictionary<string, List<TsSourceFile>>();

        foreach (var file in typeFiles)
        {
            var dir = Path.GetDirectoryName(file.FileName)?.Replace('\\', '/') ?? "";
            if (!dirToFiles.TryGetValue(dir, out var list))
            {
                list = [];
                dirToFiles[dir] = list;
            }

            list.Add(file);
        }

        var indexFiles = new List<TsSourceFile>();

        foreach (var (dir, files) in dirToFiles)
        {
            var exports = new List<TsTopLevel>();

            foreach (var file in files.OrderBy(f => f.FileName))
            {
                var moduleName = Path.GetFileNameWithoutExtension(file.FileName);

                // Collect all exported names from this file. If a name has BOTH a value and a
                // type form (e.g., StringEnum: const + type alias, InlineWrapper: namespace + type),
                // re-export as value (declaration merging on the import side).
                var valueNames = new HashSet<string>(StringComparer.Ordinal);
                var typeOnlyNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (var stmt in file.Statements)
                {
                    var name = GetExportedName(stmt);
                    if (name is null) continue;

                    if (IsTypeOnlyExport(stmt))
                        typeOnlyNames.Add(name);
                    else
                        valueNames.Add(name);
                }

                // A name that's both a value and a type → only emit as value (the import
                // pulls both via TS declaration merging).
                typeOnlyNames.ExceptWith(valueNames);

                if (valueNames.Count > 0)
                    exports.Add(new TsReExport([.. valueNames.OrderBy(n => n)], $"./{moduleName}"));

                if (typeOnlyNames.Count > 0)
                    exports.Add(new TsReExport([.. typeOnlyNames.OrderBy(n => n)], $"./{moduleName}", TypeOnly: true));
            }

            // Leaf-only barrels: do NOT re-export subdirectories. Consumers must use full
            // paths (e.g., `import { Issue } from "package/issues/domain/issue"`) or import
            // from the leaf barrel (`from "package/issues/domain"`).

            // Skip barrel generation if a user-defined type would collide with the barrel
            // file name (e.g., a type named "Index" produces "index.ts" already).
            var hasIndexCollision = files.Any(f =>
                Path.GetFileName(f.FileName).Equals("index.ts", StringComparison.OrdinalIgnoreCase));
            if (hasIndexCollision)
                continue;

            if (exports.Count > 0)
            {
                var indexPath = dir.Length > 0 ? $"{dir}/index.ts" : "index.ts";
                indexFiles.Add(new TsSourceFile(indexPath, exports, ""));
            }
        }

        return indexFiles;
    }

    private static string? GetExportedName(TsTopLevel node) => node switch
    {
        TsClass { Exported: true } c => c.Name,
        TsFunction { Exported: true } f => f.Name,
        TsEnum { Exported: true } e => e.Name,
        TsTypeAlias { Exported: true } t => t.Name,
        TsInterface { Exported: true } i => i.Name,
        TsConstObject { Exported: true } co => co.Name,
        TsNamespaceDeclaration { Exported: true } ns => ns.Name,
        _ => null
    };

    /// <summary>
    /// Returns true if the export is type-only (no runtime value).
    /// Type aliases and interfaces are type-only. Classes, functions, enums, const objects,
    /// and namespaces are values.
    /// </summary>
    private static bool IsTypeOnlyExport(TsTopLevel node) => node is TsTypeAlias or TsInterface;
}
