using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Walks the generated TypeScript top-level statements for a single file, collects every
/// referenced identifier (types AND values), and produces the corresponding
/// <see cref="TsImport"/> list.
///
/// Imports come from several sources, in priority order:
/// <list type="number">
///   <item>Implicit runtime imports (<c>HashCode</c> for records, <c>Temporal</c>, runtime type checks, <c>Enumerable</c>, <c>HashSet</c>, …)</item>
///   <item>BCL export mappings (e.g., <c>decimal</c> → <c>Decimal</c> from <c>decimal.js</c>)</item>
///   <item>External imports declared via <c>[Import("name", from: "module")]</c></item>
///   <item>Generated guard functions for transpilable types</item>
///   <item>Other transpilable types in the project (subpath imports via <see cref="PathNaming"/>)</item>
/// </list>
/// </summary>
public sealed class ImportCollector(
    IReadOnlyDictionary<string, INamedTypeSymbol> transpilableTypeMap,
    IReadOnlyDictionary<string, (string Name, string From, bool IsDefault)> externalImportMap,
    IReadOnlyDictionary<string, (string ExportedName, string FromPackage)> bclExportMap,
    IReadOnlyDictionary<string, string> guardNameToTypeMap,
    PathNaming pathNaming)
{
    private readonly IReadOnlyDictionary<string, INamedTypeSymbol> _transpilableTypeMap = transpilableTypeMap;
    private readonly IReadOnlyDictionary<string, (string Name, string From, bool IsDefault)> _externalImportMap = externalImportMap;
    private readonly IReadOnlyDictionary<string, (string ExportedName, string FromPackage)> _bclExportMap = bclExportMap;
    private readonly IReadOnlyDictionary<string, string> _guardNameToTypeMap = guardNameToTypeMap;
    private readonly PathNaming _pathNaming = pathNaming;

    public IReadOnlyList<TsImport> Collect(INamedTypeSymbol currentType, List<TsTopLevel> statements)
    {
        var referencedTypes = new HashSet<string>();
        var valueTypes = new HashSet<string>(); // types used via `new` or `extends` (need runtime import)
        var runtimeHelpers = new HashSet<string>(); // identifiers from TsTemplate.RuntimeImports
        var crossPackageOrigins = new Dictionary<string, TsTypeOrigin>(); // name → cross-package origin
        CollectReferencedTypeNames(statements, referencedTypes, valueTypes, runtimeHelpers, crossPackageOrigins);

        var tsTypeName = TypeTransformer.GetTsTypeName(currentType);
        referencedTypes.Remove(currentType.Name);
        referencedTypes.Remove(tsTypeName);
        referencedTypes.Remove($"is{tsTypeName}"); // own guard — don't import

        var imports = new List<TsImport>();
        var currentNs = PathNaming.GetNamespace(currentType);

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

        // Runtime helper imports collected from TsTemplate.RuntimeImports declarations
        // (e.g., dayNumber, listRemove, immutableInsert, immutableRemoveAt, immutableRemove).
        // Bundled into a single import line from @meta-sharp/runtime.
        if (runtimeHelpers.Count > 0)
        {
            imports.Add(new TsImport(runtimeHelpers.OrderBy(n => n).ToArray(), "@meta-sharp/runtime"));
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
        var importedNames = new HashSet<string>(runtimeTypeChecks) { "Enumerable", "Grouping", "HashSet" };
        foreach (var helper in runtimeHelpers)
            importedNames.Add(helper);

        // Cross-package imports collected via TsTypeOrigin (resolved at type-mapping
        // time, no string lookup needed). Each origin already carries the full path.
        foreach (var (typeName, origin) in crossPackageOrigins.OrderBy(kv => kv.Key))
        {
            if (!importedNames.Add(typeName)) continue;
            var importPath = $"{origin.PackageName}/{origin.SubPath}";
            imports.Add(new TsImport([typeName], importPath, IsDefault: origin.IsDefault));
        }

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

            // External import mapping ([Import] attribute, with optional AsDefault)
            if (_externalImportMap.TryGetValue(typeName, out var extImport)
                && importedNames.Add(typeName))
            {
                imports.Add(new TsImport(
                    [extImport.Name],
                    extImport.From,
                    IsDefault: extImport.IsDefault));
                continue;
            }

            // Guard function reference (e.g., isCurrency → import from Currency's file)
            if (_guardNameToTypeMap.TryGetValue(typeName, out var guardedTypeName)
                && _transpilableTypeMap.TryGetValue(guardedTypeName, out var guardedSymbol)
                && importedNames.Add(typeName))
            {
                var guardNs = PathNaming.GetNamespace(guardedSymbol);
                var guardTsName = TypeTransformer.GetTsTypeName(guardedSymbol);
                var guardPath = _pathNaming.ComputeRelativeImportPath(currentNs, guardNs, guardTsName);
                imports.Add(new TsImport([typeName], guardPath));
                continue;
            }

            // Transpilable type within the project
            if (!_transpilableTypeMap.TryGetValue(typeName, out var referencedSymbol))
                continue;

            if (!importedNames.Add(typeName)) continue;

            var targetNs = PathNaming.GetNamespace(referencedSymbol);
            var targetTsName = TypeTransformer.GetTsTypeName(referencedSymbol);
            var importPath = _pathNaming.ComputeRelativeImportPath(currentNs, targetNs, targetTsName);
            // StringEnums generate const objects — always import as value
            var isStringEnum = SymbolHelper.HasStringEnum(referencedSymbol);
            var typeOnly = !valueTypes.Contains(typeName) && !isStringEnum;
            imports.Add(new TsImport([targetTsName], importPath, TypeOnly: typeOnly));
        }

        return imports;
    }

    // ─── Reference walker (pure / static) ───────────────────

    private static void CollectReferencedTypeNames(
        IEnumerable<TsTopLevel> statements,
        HashSet<string> names,
        HashSet<string> valueNames,
        HashSet<string> runtimeHelpers,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins)
    {
        foreach (var stmt in statements)
            CollectFromTopLevel(stmt, names, valueNames, runtimeHelpers, crossPackageOrigins);
    }

    private static void CollectFromTopLevel(TsTopLevel node, HashSet<string> names, HashSet<string> valueNames, HashSet<string> runtimeHelpers, Dictionary<string, TsTypeOrigin> crossPackageOrigins)
    {
        switch (node)
        {
            case TsInterface iface:
                CollectFromTypeParameters(iface.TypeParameters, names, crossPackageOrigins);
                foreach (var prop in iface.Properties)
                    CollectFromType(prop.Type, names, crossPackageOrigins);
                if (iface.Methods is not null)
                    foreach (var method in iface.Methods)
                    {
                        CollectFromTypeParameters(method.TypeParameters, names, crossPackageOrigins);
                        foreach (var p in method.Parameters)
                            CollectFromType(p.Type, names, crossPackageOrigins);
                        CollectFromType(method.ReturnType, names, crossPackageOrigins);
                    }
                break;
            case TsFunction func:
                CollectFromTypeParameters(func.TypeParameters, names, crossPackageOrigins);
                foreach (var param in func.Parameters)
                    CollectFromType(param.Type, names, crossPackageOrigins);
                CollectFromType(func.ReturnType, names, crossPackageOrigins);
                CollectFromStatements(func.Body, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsConstObject constObj:
                foreach (var (_, value) in constObj.Entries)
                    CollectFromExpression(value, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsNamespaceDeclaration ns:
                foreach (var func in ns.Functions)
                {
                    foreach (var p in func.Parameters)
                        CollectFromType(p.Type, names, crossPackageOrigins);
                    CollectFromType(func.ReturnType, names, crossPackageOrigins);
                    CollectFromStatements(func.Body, names, valueNames, runtimeHelpers, crossPackageOrigins);
                }
                break;
            case TsClass cls:
                CollectFromTypeParameters(cls.TypeParameters, names, crossPackageOrigins);
                if (cls.Extends is not null)
                {
                    CollectFromType(cls.Extends, names, crossPackageOrigins);
                    if (cls.Extends is TsNamedType extendsNamed)
                        valueNames.Add(extendsNamed.Name);
                }
                if (cls.Implements is not null)
                    foreach (var iface in cls.Implements)
                        CollectFromType(iface, names, crossPackageOrigins);
                if (cls.Constructor is not null)
                {
                    foreach (var p in cls.Constructor.Parameters)
                        CollectFromType(p.Type, names, crossPackageOrigins);
                    CollectFromStatements(cls.Constructor.Body, names, valueNames, runtimeHelpers, crossPackageOrigins);
                }
                foreach (var member in cls.Members)
                {
                    switch (member)
                    {
                        case TsMethodMember m:
                            CollectFromTypeParameters(m.TypeParameters, names, crossPackageOrigins);
                            foreach (var p in m.Parameters)
                                CollectFromType(p.Type, names, crossPackageOrigins);
                            CollectFromType(m.ReturnType, names, crossPackageOrigins);
                            CollectFromStatements(m.Body, names, valueNames, runtimeHelpers, crossPackageOrigins);
                            // Collect from overload signatures too
                            if (m.Overloads is not null)
                                foreach (var overload in m.Overloads)
                                {
                                    foreach (var p in overload.Parameters)
                                        CollectFromType(p.Type, names, crossPackageOrigins);
                                    CollectFromType(overload.ReturnType, names, crossPackageOrigins);
                                }
                            break;
                        case TsGetterMember g:
                            CollectFromType(g.ReturnType, names, crossPackageOrigins);
                            CollectFromStatements(g.Body, names, valueNames, runtimeHelpers, crossPackageOrigins);
                            break;
                        case TsFieldMember f:
                            CollectFromType(f.Type, names, crossPackageOrigins);
                            if (f.Initializer is not null)
                                CollectFromExpression(f.Initializer, names, valueNames, runtimeHelpers, crossPackageOrigins);
                            break;
                    }
                }
                break;
            case TsTopLevelStatement topStmt:
                // The body of a [ModuleEntryPoint] method ends up here as a flat list
                // of top-level statements. Walk each one as a regular statement so any
                // identifiers it references (e.g., `new Hono()`) are picked up for
                // import emission.
                CollectFromStatement(topStmt.Inner, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsModuleExport:
                // `export default name;` / `export { name };` references a binding that
                // already exists locally — no external import to collect.
                break;
        }
    }

    private static void CollectFromTypeParameters(
        IReadOnlyList<TsTypeParameter>? typeParams,
        HashSet<string> names,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins)
    {
        if (typeParams is null) return;
        foreach (var tp in typeParams)
        {
            if (tp.Constraint is not null)
                CollectFromType(tp.Constraint, names, crossPackageOrigins);
        }
    }

    private static void CollectFromType(
        TsType? type,
        HashSet<string> names,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins)
    {
        if (type is null) return;
        switch (type)
        {
            case TsNamedType named:
                // If this type came from a cross-package source, register its origin
                // and DON'T add the simple name to `names` — we don't want the string
                // resolution loop to try (and fail) to find a local type by that name.
                if (named.Origin is not null)
                {
                    crossPackageOrigins[named.Name] = named.Origin;
                }
                else
                {
                    names.Add(named.Name);
                    // For nested types like "Outer.Inner", also add the root name "Outer"
                    // (the import is for the outer type, accessed via declaration merging).
                    if (named.Name.Contains('.'))
                    {
                        var rootName = named.Name[..named.Name.IndexOf('.')];
                        names.Add(rootName);
                    }
                }
                if (named.TypeArguments is not null)
                    foreach (var arg in named.TypeArguments)
                        CollectFromType(arg, names, crossPackageOrigins);
                break;
            case TsArrayType array:
                CollectFromType(array.ElementType, names, crossPackageOrigins);
                break;
            case TsPromiseType promise:
                CollectFromType(promise.Inner, names, crossPackageOrigins);
                break;
            case TsUnionType union:
                foreach (var t in union.Types)
                    CollectFromType(t, names, crossPackageOrigins);
                break;
            case TsIntersectionType intersection:
                foreach (var t in intersection.Types)
                    CollectFromType(t, names, crossPackageOrigins);
                break;
        }
    }

    private static void CollectFromStatements(IReadOnlyList<TsStatement> statements, HashSet<string> names, HashSet<string> valueNames, HashSet<string> runtimeHelpers, Dictionary<string, TsTypeOrigin> crossPackageOrigins)
    {
        foreach (var stmt in statements)
            CollectFromStatement(stmt, names, valueNames, runtimeHelpers, crossPackageOrigins);
    }

    private static void CollectFromStatement(TsStatement stmt, HashSet<string> names, HashSet<string> valueNames, HashSet<string> runtimeHelpers, Dictionary<string, TsTypeOrigin> crossPackageOrigins)
    {
        switch (stmt)
        {
            case TsReturnStatement ret:
                if (ret.Expression is not null) CollectFromExpression(ret.Expression, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsThrowStatement thr:
                CollectFromExpression(thr.Expression, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsExpressionStatement expr:
                CollectFromExpression(expr.Expression, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsIfStatement ifStmt:
                CollectFromExpression(ifStmt.Condition, names, valueNames, runtimeHelpers, crossPackageOrigins);
                CollectFromStatements(ifStmt.Then, names, valueNames, runtimeHelpers, crossPackageOrigins);
                if (ifStmt.Else is not null) CollectFromStatements(ifStmt.Else, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsVariableDeclaration varDecl:
                CollectFromExpression(varDecl.Initializer, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
        }
    }

    private static void CollectFromExpression(TsExpression expr, HashSet<string> names, HashSet<string> valueNames, HashSet<string> runtimeHelpers, Dictionary<string, TsTypeOrigin> crossPackageOrigins)
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
                    CollectFromExpression(arg, names, valueNames, runtimeHelpers, crossPackageOrigins);
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
                    CollectFromExpression(call.Callee, names, valueNames, runtimeHelpers, crossPackageOrigins);
                }
                else
                {
                    CollectFromExpression(call.Callee, names, valueNames, runtimeHelpers, crossPackageOrigins);
                }
                foreach (var arg in call.Arguments)
                    CollectFromExpression(arg, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsPropertyAccess access:
                CollectFromExpression(access.Object, names, valueNames, runtimeHelpers, crossPackageOrigins);
                // Static member access like IssuePriority.High → collect the type as value
                if (access.Object is TsIdentifier { Name: var propObjName }
                    && propObjName.Length > 0 && char.IsUpper(propObjName[0]))
                {
                    names.Add(propObjName);
                    valueNames.Add(propObjName);
                }
                break;
            case TsBinaryExpression bin:
                CollectFromExpression(bin.Left, names, valueNames, runtimeHelpers, crossPackageOrigins);
                CollectFromExpression(bin.Right, names, valueNames, runtimeHelpers, crossPackageOrigins);
                // instanceof uses the type as a value
                if (bin.Operator == "instanceof" && bin.Right is TsIdentifier instanceId)
                    valueNames.Add(instanceId.Name);
                break;
            case TsObjectLiteral obj:
                foreach (var prop in obj.Properties)
                    CollectFromExpression(prop.Value, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsTemplateLiteral tmpl:
                foreach (var e in tmpl.Expressions)
                    CollectFromExpression(e, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsConditionalExpression cond:
                CollectFromExpression(cond.Condition, names, valueNames, runtimeHelpers, crossPackageOrigins);
                CollectFromExpression(cond.WhenTrue, names, valueNames, runtimeHelpers, crossPackageOrigins);
                CollectFromExpression(cond.WhenFalse, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsAwaitExpression await_:
                CollectFromExpression(await_.Expression, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsParenthesized paren:
                CollectFromExpression(paren.Expression, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsSpreadExpression spread:
                CollectFromExpression(spread.Expression, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsArrowFunction arrow:
                foreach (var p in arrow.Parameters)
                    CollectFromType(p.Type, names, crossPackageOrigins);
                CollectFromStatements(arrow.Body, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsElementAccess elemAccess:
                CollectFromExpression(elemAccess.Object, names, valueNames, runtimeHelpers, crossPackageOrigins);
                CollectFromExpression(elemAccess.Index, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsArrayLiteral arrayLit:
                foreach (var e in arrayLit.Elements)
                    CollectFromExpression(e, names, valueNames, runtimeHelpers, crossPackageOrigins);
                break;
            case TsTemplate template:
                // Templates carry real TS expression nodes for the receiver and each
                // argument; the printer expands them into the call site, so the import
                // collector must descend into them too. Without this case, identifiers
                // referenced only inside [Emit] / [MapMethod]/[MapProperty] templates
                // would either be missed entirely or be marked as type-only when they
                // need to be value imports (e.g., a `new SomeRecord(...)` substituted
                // into the template).
                if (template.Receiver is not null)
                    CollectFromExpression(template.Receiver, names, valueNames, runtimeHelpers, crossPackageOrigins);
                foreach (var arg in template.Arguments)
                    CollectFromExpression(arg, names, valueNames, runtimeHelpers, crossPackageOrigins);
                // Runtime helper identifiers carried alongside the template (e.g.,
                // "dayNumber", "listRemove", "immutableInsert") from
                // [MapMethod(..., RuntimeImports = "...")] declarations. The walker can't
                // see these inside the opaque template text, so the BclMapper threads
                // them through here as a separate field. They go into the dedicated
                // `runtimeHelpers` set so the Collect entry point can emit a single
                // bundled `import { ... } from "@meta-sharp/runtime"` line.
                foreach (var helper in template.RuntimeImports)
                    runtimeHelpers.Add(helper);
                break;
        }
    }

    /// <summary>
    /// Checks if a name is a runtime type check function from <c>@meta-sharp/runtime</c>.
    /// </summary>
    private static bool IsRuntimeTypeCheck(string name) => name is
        "isChar" or "isString" or "isByte" or "isSByte"
        or "isInt16" or "isUInt16" or "isInt32" or "isUInt32"
        or "isInt64" or "isUInt64" or "isFloat32" or "isFloat64"
        or "isBool" or "isBigInt";
}
