using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Transformation;

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
    TypeScriptTransformContext context,
    IReadOnlySet<IrRuntimeRequirement>? irRuntimeRequirements = null
)
{
    private readonly TypeScriptTransformContext _context = context;
    private readonly TypeMappingContext _typeMappingContext =
        context.TypeMapping
        ?? throw new InvalidOperationException(
            "ImportCollector requires TypeScriptTransformContext.TypeMapping to be set."
        );
    private readonly IReadOnlySet<IrRuntimeRequirement>? _irRuntimeRequirements =
        irRuntimeRequirements;

    public IReadOnlyList<TsImport> Collect(
        INamedTypeSymbol currentType,
        List<TsTopLevel> statements
    )
    {
        var referencedTypes = new HashSet<string>();
        var valueTypes = new HashSet<string>(); // types used via `new` or `extends` (need runtime import)
        var runtimeHelpers = new HashSet<string>(); // identifiers from TsTemplate.RuntimeImports
        var crossPackageOrigins = new Dictionary<string, TsTypeOrigin>(); // name → cross-package origin
        CollectReferencedTypeNames(
            statements,
            referencedTypes,
            valueTypes,
            runtimeHelpers,
            crossPackageOrigins
        );

        var tsTypeName = _context.ResolveTsName(currentType);
        referencedTypes.Remove(currentType.Name);
        referencedTypes.Remove(tsTypeName);
        referencedTypes.Remove($"is{tsTypeName}"); // own guard — don't import

        var imports = new List<TsImport>();
        // For compiler-synthesized types (top-level statements), the namespace is empty.
        // Use the project's root namespace so same-namespace imports produce relative paths
        // instead of barrel aliases.
        var currentNs = PathNaming.GetNamespace(currentType);
        if (currentNs.Length == 0 && _context.PathNaming.RootNamespace.Length > 0)
            currentNs = _context.PathNaming.RootNamespace;
        // The current file's "key" — file name + namespace — used to elide self-imports
        // for types co-located via [EmitInFile]. Without this, a multi-type file would
        // try to import its sibling types from their individual paths (which don't
        // exist as separate files when the grouping kicks in).
        var currentFileName = GetFileName(currentType);

        // The IR runtime requirement scanner owns helpers visible from type
        // signatures (HashCode for records, Temporal for date types, UUID for
        // Guid, HashSet for sets, Grouping for LINQ groupings). Those land
        // first so the import line ordering matches the legacy output
        // (runtime helpers above intra-project / cross-package imports).
        if (_irRuntimeRequirements is { Count: > 0 } reqs)
            imports.AddRange(IrRuntimeRequirementToTsImport.Convert(reqs));

        // The walker below still covers cases the IR scanner doesn't see:
        //   - template-driven RuntimeImports (declared on [Emit] / [MapMethod])
        //   - Enumerable / Grouping when they appear only in expression bodies
        //     (lambda parameter type annotations, fluent chains) — the scanner
        //     finds them only on type signatures
        //   - UUID inside expression bodies emitted by the legacy
        //     JsonSerializerContextTransformer (doesn't flow through IR yet).
        var alreadyImported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var imp in imports)
        foreach (var name in imp.Names)
            alreadyImported.Add(name);

        if (referencedTypes.Contains("Enumerable") && !alreadyImported.Contains("Enumerable"))
        {
            imports.Add(new TsImport(["Enumerable"], "metano-runtime"));
            alreadyImported.Add("Enumerable");
        }

        if (referencedTypes.Contains("Grouping") && !alreadyImported.Contains("Grouping"))
        {
            imports.Add(new TsImport(["Grouping"], "metano-runtime", TypeOnly: true));
            alreadyImported.Add("Grouping");
        }

        if (
            (referencedTypes.Contains("UUID") || runtimeHelpers.Contains("UUID"))
            && !alreadyImported.Contains("UUID")
        )
        {
            imports.Add(new TsImport(["UUID"], "metano-runtime"));
            alreadyImported.Add("UUID");
            runtimeHelpers.Remove("UUID");
        }

        // Runtime helper imports collected from TsTemplate.RuntimeImports declarations
        // (e.g., dayNumber, listRemove, immutableInsert, immutableRemoveAt, immutableRemove).
        // Bundled into a single import line from metano-runtime.
        if (runtimeHelpers.Count > 0)
        {
            imports.Add(new TsImport(runtimeHelpers.OrderBy(n => n).ToArray(), "metano-runtime"));
        }

        // Track what we've already imported to avoid duplicates downstream.
        var importedNames = new HashSet<string>(alreadyImported);
        foreach (var helper in runtimeHelpers)
            importedNames.Add(helper);

        // Cross-package imports collected via TsTypeOrigin (resolved at type-mapping
        // time, no string lookup needed). Each origin carries the package name plus
        // the namespace barrel subpath; multiple type names that share that path are
        // merged into a single named-import line. Default imports are kept
        // separate because the syntax `import Foo from "..."` only supports one name.
        var byPath = new Dictionary<string, (List<string> Names, bool IsDefault)>();
        foreach (var (typeName, origin) in crossPackageOrigins)
        {
            if (!importedNames.Add(typeName))
                continue;
            var importPath =
                origin.SubPath.Length > 0
                    ? $"{origin.PackageName}/{origin.SubPath}"
                    : origin.PackageName;
            // Default imports never merge — emit them as their own line.
            if (origin.IsDefault)
            {
                imports.Add(new TsImport([typeName], importPath, IsDefault: true));
                continue;
            }
            if (!byPath.TryGetValue(importPath, out var bucket))
            {
                bucket = (new List<string>(), false);
                byPath[importPath] = bucket;
            }
            bucket.Names.Add(typeName);
        }
        foreach (var (importPath, bucket) in byPath.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // Per-name type-only detection. With `verbatimModuleSyntax: true` in the
            // consumer, importing a type-only reference as a value triggers TS2749 /
            // TS1484, so we mark each name independently:
            //
            //   - All names type-only       → `import type { … }`        (whole stmt)
            //   - All names value           → `import { … }`             (plain)
            //   - Mixed                     → `import { A, type B }`     (per-name)
            //
            // The mixed form requires extending the named-import emission with the
            // inline `type` qualifier, which the printer handles via TsImport.TypeOnlyNames.
            var typeOnlyNames = bucket
                .Names.Where(n => !valueTypes.Contains(n))
                .ToHashSet(StringComparer.Ordinal);
            var allTypeOnly = typeOnlyNames.Count == bucket.Names.Count;
            var anyTypeOnly = typeOnlyNames.Count > 0;
            SortValuesFirst(bucket.Names, typeOnlyNames);

            imports.Add(
                new TsImport(
                    bucket.Names.ToArray(),
                    importPath,
                    TypeOnly: allTypeOnly,
                    TypeOnlyNames: !allTypeOnly && anyTypeOnly ? typeOnlyNames : null
                )
            );
        }

        // Local imports (guards + transpilable types within the project) are bucketed
        // by import path so multiple names from the same barrel collapse into one line.
        // Same strategy the cross-package loop above uses — keeps barrel-first output
        // clean and Biome-friendly (e.g., all-type-only → whole-statement `import type`
        // rather than per-name `{ type A, type B }`).
        var localByPath = new Dictionary<
            string,
            (List<string> Names, HashSet<string> TypeOnlyNames)
        >(StringComparer.Ordinal);

        // Per-specifier dedup inside a bucket. The outer `importedNames` set dedupes
        // by the *referenced* identifier (the key used to look up the symbol), but
        // `_context.TranspilableTypeMap` is dual-keyed by C# name AND TS name when [Name]
        // diverges, so two distinct lookup keys can resolve to the same `targetTsName`
        // and attempt to add it twice. Without this guard the output would be
        // `import { Foo, Foo } from "..."`. Also: if a specifier is observed as both
        // value and type-only across iterations, the value form wins — type-only is
        // the narrower claim, and runtime-imported names must not be demoted to
        // type-only imports (would break `new Foo(...)` at runtime).
        void AddLocal(string importPath, string name, bool typeOnly)
        {
            if (!localByPath.TryGetValue(importPath, out var bucket))
            {
                bucket = (new List<string>(), new HashSet<string>(StringComparer.Ordinal));
                localByPath[importPath] = bucket;
            }
            if (!bucket.Names.Contains(name))
                bucket.Names.Add(name);
            if (typeOnly)
                bucket.TypeOnlyNames.Add(name);
            else
                bucket.TypeOnlyNames.Remove(name);
        }

        foreach (var typeName in referencedTypes.OrderBy(n => n))
        {
            // Skip built-in types and runtime identifiers that don't need imports
            if (
                typeName.StartsWith("Temporal.")
                || IsRuntimeTypeCheck(typeName)
                || typeName
                    is "Map"
                        or "Set"
                        or "unknown"
                        or "any"
                        or "null"
                        or "Partial"
                        or "Error"
                        or "HashCode"
                        or "Array"
                        or "v"
                        or "value"
                        or "true"
                        or "false"
                        or "undefined"
                        or "console"
                        or "Math"
                        or "crypto"
                        or "Object"
                        or "typeof"
                        or "unknown[]"
                        or "delegateAdd"
                        or "delegateRemove"
            )
                continue;

            // BCL export mapping (e.g., decimal → Decimal from "decimal.js").
            // Not bucketed: each BCL mapping can land on a distinct external package,
            // and the cross-package merge strategy doesn't apply here.
            var bclEntry = _context.BclExportMap.Values.FirstOrDefault(e =>
                e.ExportedName == typeName
            );
            if (
                bclEntry is not null
                && bclEntry.FromPackage.Length > 0
                && importedNames.Add(typeName)
            )
            {
                imports.Add(new TsImport([bclEntry.ExportedName], bclEntry.FromPackage));
                continue;
            }

            // External import mapping ([Import] attribute, with optional AsDefault).
            // Not bucketed: default imports never merge (`import Foo from "..."` is
            // one-name-only) and named externals come from arbitrary modules.
            if (
                _context.ExternalImportMap.TryGetValue(typeName, out var extImport)
                && importedNames.Add(typeName)
            )
            {
                imports.Add(
                    new TsImport([extImport.Name], extImport.From, IsDefault: extImport.IsDefault)
                );
                // Track for auto-deps when [Import] declared a Version. The package
                // name is `extImport.From` (the module specifier).
                if (extImport.Version is not null && extImport.Version.Length > 0)
                    _typeMappingContext.UsedCrossPackages[extImport.From] = extImport.Version;
                continue;
            }

            // Guard function reference (e.g., isCurrency → import from Currency's file).
            // Bucketed alongside transpilable types so a file that uses both `Currency`
            // and `isCurrency` ends up with a single `import { Currency, isCurrency }`
            // line instead of two.
            if (
                _context.GuardNameToTypeMap.TryGetValue(typeName, out var guardedTypeName)
                && _context.TranspilableTypeMap.TryGetValue(guardedTypeName, out var guardedSymbol)
                && importedNames.Add(typeName)
            )
            {
                var guardNs = PathNaming.GetNamespace(guardedSymbol);
                // Use the file name (not the type name) when computing the path so a
                // guard for a [EmitInFile]-grouped type points at the merged file.
                var guardFileName = GetFileName(guardedSymbol);
                if (guardNs == currentNs && guardFileName == currentFileName)
                    continue; // same file
                var guardPath = _context.PathNaming.ComputeRelativeImportPath(
                    currentNs,
                    guardNs,
                    guardFileName
                );
                // Guards are functions → always imported as values, never type-only.
                AddLocal(guardPath, typeName, typeOnly: false);
                continue;
            }

            // Transpilable type within the project
            if (!_context.TranspilableTypeMap.TryGetValue(typeName, out var referencedSymbol))
                continue;

            // Skip types co-located in the same file via [EmitInFile] — they're
            // declared locally in the merged source, no import needed.
            var targetNs = PathNaming.GetNamespace(referencedSymbol);
            var targetFileName = GetFileName(referencedSymbol);
            if (targetNs == currentNs && targetFileName == currentFileName)
                continue;

            if (!importedNames.Add(typeName))
                continue;

            var targetTsName = _context.ResolveTsName(referencedSymbol);
            // Path is computed against the FILE name (not the type name) so multiple
            // types co-located in the same file resolve to the same import path.
            var importPath = _context.PathNaming.ComputeRelativeImportPath(
                currentNs,
                targetNs,
                targetFileName
            );
            // StringEnums generate const objects — always import as value
            var isStringEnum = SymbolHelper.HasStringEnum(referencedSymbol);
            var typeOnly = !valueTypes.Contains(typeName) && !isStringEnum;
            AddLocal(importPath, targetTsName, typeOnly);
        }

        // Emit one merged TsImport per bucketed local path. Three-case form:
        //   - all type-only → `import type { A, B } from "..."`       (whole statement)
        //   - all value     → `import { A, B } from "..."`            (plain)
        //   - mixed         → `import { A, type B } from "..."`       (per-name)
        // The whole-statement form is preferred when everything is type-only so Biome's
        // `noImportTypeQualifier` lint stays quiet.
        foreach (
            var (importPath, bucket) in localByPath.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        )
        {
            var allTypeOnly = bucket.TypeOnlyNames.Count == bucket.Names.Count;
            var anyTypeOnly = bucket.TypeOnlyNames.Count > 0;
            SortValuesFirst(bucket.Names, bucket.TypeOnlyNames);
            imports.Add(
                new TsImport(
                    bucket.Names.ToArray(),
                    importPath,
                    TypeOnly: allTypeOnly,
                    TypeOnlyNames: !allTypeOnly && anyTypeOnly ? bucket.TypeOnlyNames : null
                )
            );
        }

        return MergeImportsByPath(imports);
    }

    /// <summary>
    /// Consolidates multiple <see cref="TsImport"/> entries that share the same path into
    /// a single import line. Default imports are kept separate. Type-only qualification is
    /// preserved: a name is type-only only if ALL contributing imports marked it as such.
    /// </summary>
    private static IReadOnlyList<TsImport> MergeImportsByPath(List<TsImport> imports)
    {
        var merged = new List<TsImport>();
        var grouped = imports.GroupBy(i => (i.From, i.IsDefault)).ToList();

        foreach (var group in grouped)
        {
            var items = group.ToList();
            if (items.Count == 1)
            {
                merged.Add(items[0]);
                continue;
            }

            // Default imports can't be merged (only one name per default import).
            if (group.Key.IsDefault)
            {
                merged.AddRange(items);
                continue;
            }

            // Merge all names, preserving type-only where every contributor agrees.
            var allNames = new List<string>();
            var typeOnlyNames = new HashSet<string>(StringComparer.Ordinal);
            var allTypeOnly = true;

            foreach (var item in items)
            {
                foreach (var name in item.Names)
                {
                    if (allNames.Contains(name))
                        continue;
                    allNames.Add(name);

                    var isTypeOnly =
                        item.TypeOnly
                        || (item.TypeOnlyNames is not null && item.TypeOnlyNames.Contains(name));
                    if (isTypeOnly)
                        typeOnlyNames.Add(name);
                }

                if (!item.TypeOnly)
                    allTypeOnly = false;
            }

            // Names that appear as value in ANY import are not type-only.
            if (!allTypeOnly)
            {
                foreach (var item in items)
                {
                    if (item.TypeOnly)
                        continue;
                    foreach (var name in item.Names)
                    {
                        if (item.TypeOnlyNames is null || !item.TypeOnlyNames.Contains(name))
                            typeOnlyNames.Remove(name);
                    }
                }
            }

            var finalAllTypeOnly = allTypeOnly || typeOnlyNames.Count == allNames.Count;
            SortValuesFirst(allNames, typeOnlyNames);

            merged.Add(
                new TsImport(
                    allNames.ToArray(),
                    group.Key.From,
                    TypeOnly: finalAllTypeOnly,
                    TypeOnlyNames: !finalAllTypeOnly && typeOnlyNames.Count > 0
                        ? typeOnlyNames
                        : null
                )
            );
        }

        return merged;
    }

    /// <summary>
    /// Sorts named-import entries with values first (alphabetical), then type-only
    /// names (alphabetical). In all-value or all-type-only buckets this reduces to a
    /// plain alphabetical sort. In mixed buckets it produces the more readable
    /// <c>{ Value, type Type }</c> shape instead of interleaving them by name.
    /// </summary>
    private static void SortValuesFirst(List<string> names, IReadOnlySet<string> typeOnlyNames)
    {
        names.Sort(
            (a, b) =>
            {
                var aIsType = typeOnlyNames.Contains(a);
                var bIsType = typeOnlyNames.Contains(b);
                if (aIsType != bIsType)
                    return aIsType ? 1 : -1;
                return string.CompareOrdinal(a, b);
            }
        );
    }

    /// <summary>
    /// Returns the file name (without extension, kebab-cased) under which the type
    /// would be emitted. Honors <c>[EmitInFile("name")]</c>; falls back to the type's
    /// own TS name (which itself honors <c>[Name]</c>). Used by the import collector
    /// to elide self-imports for types co-located in the same file.
    /// </summary>
    private string GetFileName(INamedTypeSymbol type)
    {
        var explicitFile = SymbolHelper.GetEmitInFile(type);
        var name =
            explicitFile is not null && explicitFile.Length > 0
                ? explicitFile
                : _context.ResolveTsName(type);
        return SymbolHelper.ToKebabCase(name);
    }

    // ─── Reference walker (pure / static) ───────────────────

    private static void CollectReferencedTypeNames(
        IEnumerable<TsTopLevel> statements,
        HashSet<string> names,
        HashSet<string> valueNames,
        HashSet<string> runtimeHelpers,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
    {
        foreach (var stmt in statements)
            CollectFromTopLevel(stmt, names, valueNames, runtimeHelpers, crossPackageOrigins);
    }

    private static void CollectFromTopLevel(
        TsTopLevel node,
        HashSet<string> names,
        HashSet<string> valueNames,
        HashSet<string> runtimeHelpers,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
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
                        CollectFromTypeParameters(
                            method.TypeParameters,
                            names,
                            crossPackageOrigins
                        );
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
                CollectFromStatements(
                    func.Body,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsConstObject constObj:
                foreach (var (_, value) in constObj.Entries)
                    CollectFromExpression(
                        value,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsNamespaceDeclaration ns:
                foreach (var func in ns.Functions)
                {
                    foreach (var p in func.Parameters)
                        CollectFromType(p.Type, names, crossPackageOrigins);
                    CollectFromType(func.ReturnType, names, crossPackageOrigins);
                    CollectFromStatements(
                        func.Body,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
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
                    CollectFromStatements(
                        cls.Constructor.Body,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
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
                            CollectFromStatements(
                                m.Body,
                                names,
                                valueNames,
                                runtimeHelpers,
                                crossPackageOrigins
                            );
                            // Collect from overload signatures too
                            if (m.Overloads is not null)
                                foreach (var overload in m.Overloads)
                                {
                                    foreach (var p in overload.Parameters)
                                        CollectFromType(p.Type, names, crossPackageOrigins);
                                    CollectFromType(
                                        overload.ReturnType,
                                        names,
                                        crossPackageOrigins
                                    );
                                }
                            break;
                        case TsGetterMember g:
                            CollectFromType(g.ReturnType, names, crossPackageOrigins);
                            CollectFromStatements(
                                g.Body,
                                names,
                                valueNames,
                                runtimeHelpers,
                                crossPackageOrigins
                            );
                            break;
                        case TsFieldMember f:
                            CollectFromType(f.Type, names, crossPackageOrigins);
                            if (f.Initializer is not null)
                                CollectFromExpression(
                                    f.Initializer,
                                    names,
                                    valueNames,
                                    runtimeHelpers,
                                    crossPackageOrigins
                                );
                            break;
                    }
                }
                break;
            case TsTopLevelStatement topStmt:
                // The body of a [ModuleEntryPoint] method ends up here as a flat list
                // of top-level statements. Walk each one as a regular statement so any
                // identifiers it references (e.g., `new Hono()`) are picked up for
                // import emission.
                CollectFromStatement(
                    topStmt.Inner,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
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
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
    {
        if (typeParams is null)
            return;
        foreach (var tp in typeParams)
        {
            if (tp.Constraint is not null)
                CollectFromType(tp.Constraint, names, crossPackageOrigins);
        }
    }

    private static void CollectFromType(
        TsType? type,
        HashSet<string> names,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
    {
        if (type is null)
            return;
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

    private static void CollectFromStatements(
        IReadOnlyList<TsStatement> statements,
        HashSet<string> names,
        HashSet<string> valueNames,
        HashSet<string> runtimeHelpers,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
    {
        foreach (var stmt in statements)
            CollectFromStatement(stmt, names, valueNames, runtimeHelpers, crossPackageOrigins);
    }

    private static void CollectFromStatement(
        TsStatement stmt,
        HashSet<string> names,
        HashSet<string> valueNames,
        HashSet<string> runtimeHelpers,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
    {
        switch (stmt)
        {
            case TsReturnStatement ret:
                if (ret.Expression is not null)
                    CollectFromExpression(
                        ret.Expression,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsThrowStatement thr:
                CollectFromExpression(
                    thr.Expression,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsExpressionStatement expr:
                CollectFromExpression(
                    expr.Expression,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsIfStatement ifStmt:
                CollectFromExpression(
                    ifStmt.Condition,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                CollectFromStatements(
                    ifStmt.Then,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                if (ifStmt.Else is not null)
                    CollectFromStatements(
                        ifStmt.Else,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsVariableDeclaration varDecl:
                CollectFromExpression(
                    varDecl.Initializer,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
        }
    }

    private static void CollectFromExpression(
        TsExpression expr,
        HashSet<string> names,
        HashSet<string> valueNames,
        HashSet<string> runtimeHelpers,
        Dictionary<string, TsTypeOrigin> crossPackageOrigins
    )
    {
        switch (expr)
        {
            case TsTypeReference typeRef:
                // Cross-package type used in expression position (e.g., the receiver of
                // a static member access like `Priority.High`). The wrapper carries the
                // origin so we can register the import without going through name-based
                // resolution.
                crossPackageOrigins[typeRef.Name] = typeRef.Origin;
                break;
            case TsIdentifier bareId:
                // Bare identifiers starting with uppercase may reference transpilable
                // types used as values (e.g., enum objects passed to descriptor specs).
                if (bareId.Name.Length > 0 && char.IsUpper(bareId.Name[0]))
                {
                    names.Add(bareId.Name);
                    valueNames.Add(bareId.Name);
                }
                break;
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
                    CollectFromExpression(
                        arg,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsCallExpression call:
                // Function calls may reference guard functions (e.g., isCurrency)
                if (call.Callee is TsIdentifier callId)
                {
                    names.Add(callId.Name);
                    valueNames.Add(callId.Name);
                }
                // Static method calls like Enumerable.from(...)
                else if (
                    call.Callee is TsPropertyAccess { Object: TsIdentifier { Name: var rootName } }
                    && char.IsUpper(rootName[0])
                )
                {
                    names.Add(rootName);
                    valueNames.Add(rootName);
                    CollectFromExpression(
                        call.Callee,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                }
                else
                {
                    CollectFromExpression(
                        call.Callee,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                }
                foreach (var arg in call.Arguments)
                    CollectFromExpression(
                        arg,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsPropertyAccess access:
                CollectFromExpression(
                    access.Object,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                // Static member access like IssuePriority.High → collect the type as value
                if (
                    access.Object is TsIdentifier { Name: var propObjName }
                    && propObjName.Length > 0
                    && char.IsUpper(propObjName[0])
                )
                {
                    names.Add(propObjName);
                    valueNames.Add(propObjName);
                }
                break;
            case TsBinaryExpression bin:
                CollectFromExpression(
                    bin.Left,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                CollectFromExpression(
                    bin.Right,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                // instanceof uses the type as a value
                if (bin.Operator == "instanceof" && bin.Right is TsIdentifier instanceId)
                    valueNames.Add(instanceId.Name);
                break;
            case TsObjectLiteral obj:
                foreach (var prop in obj.Properties)
                    CollectFromExpression(
                        prop.Value,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsTemplateLiteral tmpl:
                foreach (var e in tmpl.Expressions)
                    CollectFromExpression(
                        e,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsConditionalExpression cond:
                CollectFromExpression(
                    cond.Condition,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                CollectFromExpression(
                    cond.WhenTrue,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                CollectFromExpression(
                    cond.WhenFalse,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsAwaitExpression await_:
                CollectFromExpression(
                    await_.Expression,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsParenthesized paren:
                CollectFromExpression(
                    paren.Expression,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsSpreadExpression spread:
                CollectFromExpression(
                    spread.Expression,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsArrowFunction arrow:
                foreach (var p in arrow.Parameters)
                    CollectFromType(p.Type, names, crossPackageOrigins);
                CollectFromStatements(
                    arrow.Body,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsElementAccess elemAccess:
                CollectFromExpression(
                    elemAccess.Object,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                CollectFromExpression(
                    elemAccess.Index,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                break;
            case TsArrayLiteral arrayLit:
                foreach (var e in arrayLit.Elements)
                    CollectFromExpression(
                        e,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                break;
            case TsCastExpression cast:
                // Descend into both the inner expression AND the target type.
                // The target type can introduce a brand-new identifier that
                // appears nowhere else — e.g. `p.estimatedHours as Decimal` in a
                // generated serializer factory, where `Decimal` is referenced
                // only here and needs to end up in the import list. Without this
                // case, the walker would miss it entirely.
                CollectFromExpression(
                    cast.Expression,
                    names,
                    valueNames,
                    runtimeHelpers,
                    crossPackageOrigins
                );
                CollectFromType(cast.Type, names, crossPackageOrigins);
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
                    CollectFromExpression(
                        template.Receiver,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                foreach (var arg in template.Arguments)
                    CollectFromExpression(
                        arg,
                        names,
                        valueNames,
                        runtimeHelpers,
                        crossPackageOrigins
                    );
                // Runtime helper identifiers carried alongside the template (e.g.,
                // "dayNumber", "listRemove", "immutableInsert") from
                // [MapMethod(..., RuntimeImports = "...")] declarations. The walker can't
                // see these inside the opaque template text, so the BclMapper threads
                // them through here as a separate field. They go into the dedicated
                // `runtimeHelpers` set so the Collect entry point can emit a single
                // bundled `import { ... } from "metano-runtime"` line.
                foreach (var helper in template.RuntimeImports)
                    runtimeHelpers.Add(helper);
                break;
        }
    }

    /// <summary>
    /// Checks if a name is a runtime type check function from <c>metano-runtime</c>.
    /// </summary>
    private static bool IsRuntimeTypeCheck(string name) =>
        name
            is "isChar"
                or "isString"
                or "isByte"
                or "isSByte"
                or "isInt16"
                or "isUInt16"
                or "isInt32"
                or "isUInt32"
                or "isInt64"
                or "isUInt64"
                or "isFloat32"
                or "isFloat64"
                or "isBool"
                or "isBigInt";
}
