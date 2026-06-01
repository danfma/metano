using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

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

    /// <summary>
    /// Identifiers that surface in generated TS but never need an
    /// <c>import</c> line: TypeScript built-ins (<c>Map</c>, <c>Set</c>,
    /// <c>unknown</c>, …), JavaScript globals (<c>console</c>,
    /// <c>Math</c>, <c>crypto</c>, <c>Object</c>), keywords used as
    /// expressions (<c>typeof</c>), and runtime helpers that the
    /// codegen wires in through a dedicated path
    /// (<c>delegateAdd</c>/<c>delegateRemove</c>,
    /// <c>getUnionGuard</c>/<c>registerUnionGuard</c>, <c>HashCode</c>).
    /// Hoisted out of the original 28-arm pattern match to drop the
    /// scrolling cost when adding a new bypass.
    /// </summary>
    private static readonly HashSet<string> NonImportableIdentifiers = new(StringComparer.Ordinal)
    {
        "Map",
        "Set",
        "unknown",
        "any",
        "null",
        "Partial",
        "Error",
        "HashCode",
        "Array",
        "v",
        "value",
        "true",
        "false",
        "undefined",
        "console",
        "Math",
        "crypto",
        "Object",
        "typeof",
        "unknown[]",
        "delegateAdd",
        "delegateRemove",
        "getUnionGuard",
        "registerUnionGuard",
    };

    public IReadOnlyList<TsImport> Collect(
        INamedTypeSymbol currentType,
        List<TsTopLevel> statements
    )
    {
        var referencedTypes = new HashSet<string>();
        var valueTypes = new HashSet<string>(); // types used via `new` or `extends` (need runtime import)
        var runtimeHelpers = new HashSet<string>(); // identifiers from TsTemplate.RuntimeImports
        var crossPackageOrigins = new Dictionary<string, TsTypeOrigin>(); // name → cross-package origin
        var templateExternals = new List<IrExternalImport>();
        var linqPipeHelpers = new HashSet<string>(StringComparer.Ordinal);
        var sink = new ImportCollectionSink(
            referencedTypes,
            valueTypes,
            runtimeHelpers,
            crossPackageOrigins,
            templateExternals,
            linqPipeHelpers
        );
        CollectReferencedTypeNames(statements, sink);

        // C# `using X = Y;` aliases substitute the canonical type name with
        // the user's alias on every emitted token. The walker therefore sees
        // the alias text only; recover the canonical via the per-file scope
        // so the import line resolves to the canonical and emits the
        // matching `{ Canonical as Alias }` form.
        var aliasOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (IrToTsTypeMapper.UsingAliases is { } activeScope)
        {
            foreach (var name in referencedTypes.ToList())
            {
                if (
                    !activeScope.AliasToCanonical.TryGetValue(name, out var canonical)
                    || aliasOverrides.ContainsKey(canonical)
                )
                    continue;
                referencedTypes.Add(canonical);
                if (valueTypes.Contains(name))
                    valueTypes.Add(canonical);
                aliasOverrides[canonical] = name;
                referencedTypes.Remove(name);
                valueTypes.Remove(name);
            }
        }

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

        // `Grouping<K,T>` is shipped from `metano-runtime/system/linq` (the pipe
        // runtime) and re-exported from the root `metano-runtime` barrel.
        // Pull it in as a type-only import when the consumer file references
        // it but didn't go through the LINQ-pipe import path.
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

        // LINQ pipe helpers from TsLinqCall — `linq` itself + every
        // operator factory the chain uses. Live in the dedicated
        // subpath so bundlers tree-shake unused operators across the
        // whole runtime, not just the top-level `metano-runtime` entry.
        if (linqPipeHelpers.Count > 0)
        {
            imports.Add(
                new TsImport(
                    linqPipeHelpers.OrderBy(n => n).ToArray(),
                    "metano-runtime/system/linq"
                )
            );
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
            // An import line is actually being emitted for this package — promote
            // the resolver's staged version hint into a real package.json
            // dependency. (Types whose origin was resolved but which erased never
            // reach this loop, so their package stays out of dependencies.)
            _typeMappingContext.ConfirmCrossPackageDependency(origin.PackageName);
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
            (List<string> Names, HashSet<string> TypeOnlyNames, Dictionary<string, string> Aliases)
        >(StringComparer.Ordinal);

        // Per-specifier dedup inside a bucket. The outer `importedNames` set dedupes
        // by the *referenced* identifier (the key used to look up the entry), but
        // `_context.TranspilableTypes` is dual-keyed by C# name AND TS name when [Name]
        // diverges, so two distinct lookup keys can resolve to the same `TsName` and
        // attempt to add it twice. Without this guard the output would be
        // `import { Foo, Foo } from "..."`. Also: if a specifier is observed as both
        // value and type-only across iterations, the value form wins — type-only is
        // the narrower claim, and runtime-imported names must not be demoted to
        // type-only imports (would break `new Foo(...)` at runtime).
        void AddLocal(string importPath, string name, bool typeOnly)
        {
            if (!localByPath.TryGetValue(importPath, out var bucket))
            {
                bucket = (
                    new List<string>(),
                    new HashSet<string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                );
                localByPath[importPath] = bucket;
            }
            if (!bucket.Names.Contains(name))
                bucket.Names.Add(name);
            if (typeOnly)
                bucket.TypeOnlyNames.Add(name);
            else
                bucket.TypeOnlyNames.Remove(name);
            if (aliasOverrides.TryGetValue(name, out var aliasName))
                bucket.Aliases[name] = aliasName;
        }

        foreach (var typeName in referencedTypes.OrderBy(n => n))
        {
            // Skip built-in types and runtime identifiers that don't need imports.
            if (
                typeName.StartsWith("Temporal.")
                || IsRuntimeTypeCheck(typeName)
                || NonImportableIdentifiers.Contains(typeName)
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
            // When the C# type name differs from the npm export name, we emit
            // `import { Original as Local } from "..."` so the local binding
            // matches what the rest of the file references (e.g. C# class
            // `InfernoElement` mapped to `[Import(name: "VNode", from: "inferno")]`).
            if (
                _context.ExternalImportMap.TryGetValue(typeName, out var extImport)
                && importedNames.Add(typeName)
            )
            {
                IReadOnlyDictionary<string, string>? aliases = null;
                if (
                    !extImport.IsDefault
                    && !string.Equals(extImport.Name, typeName, StringComparison.Ordinal)
                )
                    aliases = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [extImport.Name] = typeName,
                    };
                var isTypeOnly = !extImport.IsDefault && !valueTypes.Contains(typeName);
                imports.Add(
                    new TsImport(
                        [extImport.Name],
                        extImport.From,
                        IsDefault: extImport.IsDefault,
                        TypeOnly: isTypeOnly,
                        Aliases: aliases
                    )
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
                _context.TryResolveGuardImport(typeName, out var guarded)
                && importedNames.Add(typeName)
            )
            {
                // Use the file name (not the type name) when computing the path so a
                // guard for a [EmitInFile]-grouped type points at the merged file.
                if (guarded.Namespace == currentNs && guarded.FileName == currentFileName)
                    continue; // same file
                var guardPath = _context.PathNaming.ComputeRelativeImportPath(
                    currentNs,
                    guarded.Namespace,
                    guarded.FileName
                );
                // Guards are functions → always imported as values, never type-only.
                AddLocal(guardPath, typeName, typeOnly: false);
                continue;
            }

            // Extension helper reference: a call site that lowered an
            // extension method or property (`receiver.Squared()` →
            // `squared(receiver)`) needs to import the helper from the
            // static class's emitted file. The frontend doesn't see this
            // path — the rewrite happens in the IR extractor — so the
            // resolution is keyed on the camelCase helper name registered
            // in the per-context module-helpers map.
            if (
                valueTypes.Contains(typeName)
                && _context.ExtensionHelperFunctions.TryGetValue(typeName, out var helperOwner)
                && importedNames.Add(typeName)
            )
            {
                if (helperOwner.Namespace == currentNs && helperOwner.FileName == currentFileName)
                    continue;
                var helperPath = _context.PathNaming.ComputeRelativeImportPath(
                    currentNs,
                    helperOwner.Namespace,
                    helperOwner.FileName
                );
                AddLocal(helperPath, typeName, typeOnly: false);
                continue;
            }

            // NoContainer static-method export (e.g., `column` from a
            // `[NoContainer]` UI facade). The call-site flatten already
            // dropped the type qualifier in the bridge; we look up the
            // declaring type's emit metadata so the import points at
            // the right module file with the lowercase function name
            // (NOT the type's TsName) as the imported symbol.
            //
            // Skip when the name shows up only because a `using` alias
            // remapped a colliding canonical type — the reference belongs
            // to the imported type, not to the local factory that happens
            // to share the same emitted name.
            if (aliasOverrides.ContainsKey(typeName))
            {
                // fall through to the transpilable-type branch
            }
            else if (
                _context.NoContainerFunctionExports.TryGetValue(typeName, out var erasableExport)
                && importedNames.Add(typeName)
            )
            {
                // Cross-assembly entry — route through the originating
                // [EmitPackage] subpath instead of a project-relative file path.
                if (erasableExport.CrossPackageOrigin is { } crossOrigin)
                {
                    // An import line is actually being emitted for this package —
                    // promote the resolver's staged version hint into a real
                    // package.json dependency (mirrors the cross-package origin
                    // loop above). Without this the [NoContainer] facade's package
                    // is dropped from #dependencies.
                    _typeMappingContext.ConfirmCrossPackageDependency(crossOrigin.PackageName);
                    var crossPath =
                        crossOrigin.SubPath.Length > 0
                            ? $"{crossOrigin.PackageName}/{crossOrigin.SubPath}"
                            : crossOrigin.PackageName;
                    AddLocal(crossPath, typeName, typeOnly: false);
                    continue;
                }
                // Same emit target — no import needed.
                var erasableOwner = erasableExport.Owner;
                if (
                    erasableOwner.Namespace == currentNs
                    && erasableOwner.FileName == currentFileName
                )
                    continue;
                var fnPath = _context.PathNaming.ComputeRelativeImportPath(
                    currentNs,
                    erasableOwner.Namespace,
                    erasableOwner.FileName
                );
                AddLocal(fnPath, typeName, typeOnly: false);
                continue;
            }

            // Transpilable type within the project
            if (!_context.TranspilableTypes.TryGetValue(typeName, out var referencedRef))
                continue;

            // Skip types co-located in the same file via [EmitInFile] — they're
            // declared locally in the merged source, no import needed.
            if (referencedRef.Namespace == currentNs && referencedRef.FileName == currentFileName)
                continue;

            if (!importedNames.Add(typeName))
                continue;

            // Path is computed against the FILE name (not the type name) so multiple
            // types co-located in the same file resolve to the same import path.
            var importPath = _context.PathNaming.ComputeRelativeImportPath(
                currentNs,
                referencedRef.Namespace,
                referencedRef.FileName
            );
            var typeOnly = !valueTypes.Contains(typeName);
            AddLocal(importPath, referencedRef.TsName, typeOnly);
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
                    TypeOnlyNames: !allTypeOnly && anyTypeOnly ? bucket.TypeOnlyNames : null,
                    Aliases: bucket.Aliases.Count > 0 ? bucket.Aliases : null
                )
            );
        }

        // Template-driven external imports collected from TsTemplate.ExternalImports
        // (e.g., `[Emit("createElement($T0, $0)"), Import("createElement", "inferno-create-element")]`).
        // The walker can't see identifiers buried inside the opaque template text, so the
        // IR extractor threads them through here as a separate list. Dedupe by (name, from)
        // and skip names already emitted by the main loop above so we don't double-import.
        if (templateExternals.Count > 0)
        {
            var seenTemplateExternals =
                new HashSet<(string Name, string From, bool IsDefault, string? EmittedName)>();
            foreach (var ext in templateExternals)
            {
                var bindingName = ext.EmittedName ?? ext.Name;
                if (importedNames.Contains(bindingName))
                    continue;
                if (
                    !seenTemplateExternals.Add((ext.Name, ext.From, ext.IsDefault, ext.EmittedName))
                )
                    continue;

                var aliases = BuildExternalImportAliases(ext);

                imports.Add(
                    new TsImport([ext.Name], ext.From, IsDefault: ext.IsDefault, Aliases: aliases)
                );
                importedNames.Add(bindingName);
                if (ext.Version is { Length: > 0 } version)
                    _typeMappingContext.UsedCrossPackages[ext.From] = version;
            }
        }

        return MergeImportsByPath(imports);
    }

    private static IReadOnlyDictionary<string, string>? BuildExternalImportAliases(
        IrExternalImport ext
    )
    {
        var divergent = SymbolHelper.NormalizeDivergentName(ext.EmittedName, ext.Name);
        return divergent is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { [ext.Name] = divergent };
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
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
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

                if (item.Aliases is not null)
                    foreach (var (original, local) in item.Aliases)
                        aliases[original] = local;

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
                        : null,
                    Aliases: aliases.Count > 0 ? aliases : null
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

    private void CollectReferencedTypeNames(
        IEnumerable<TsTopLevel> statements,
        ImportCollectionSink sink
    )
    {
        foreach (var stmt in statements)
            CollectFromTopLevel(stmt, sink);
    }

    private void CollectFromTopLevel(TsTopLevel node, ImportCollectionSink sink)
    {
        var names = sink.Names;
        var valueNames = sink.ValueNames;
        var runtimeHelpers = sink.RuntimeHelpers;
        var crossPackageOrigins = sink.CrossPackageOrigins;
        switch (node)
        {
            case TsTypeAlias alias:
                CollectFromTypeParameters(alias.TypeParameters, names, crossPackageOrigins);
                CollectFromType(alias.Type, names, crossPackageOrigins);
                break;
            case TsInterface iface:
                CollectFromTypeParameters(iface.TypeParameters, names, crossPackageOrigins);
                if (iface.Extends is not null)
                    foreach (var baseRef in iface.Extends)
                        CollectFromType(baseRef, names, crossPackageOrigins);
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
                CollectFromStatements(func.Body, sink);
                break;
            case TsConstObject constObj:
                foreach (var (_, value) in constObj.Entries)
                    CollectFromExpression(value, sink);
                break;
            case TsNamespaceDeclaration ns:
                foreach (var func in ns.Functions)
                {
                    foreach (var p in func.Parameters)
                        CollectFromType(p.Type, names, crossPackageOrigins);
                    CollectFromType(func.ReturnType, names, crossPackageOrigins);
                    CollectFromStatements(func.Body, sink);
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
                    CollectFromStatements(cls.Constructor.Body, sink);
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
                            CollectFromStatements(m.Body, sink);
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
                            CollectFromStatements(g.Body, sink);
                            break;
                        case TsFieldMember f:
                            CollectFromType(f.Type, names, crossPackageOrigins);
                            if (f.Initializer is not null)
                                CollectFromExpression(f.Initializer, sink);
                            break;
                    }
                }
                break;
            case TsTopLevelStatement topStmt:
                // The body of a [ModuleEntryPoint] method ends up here as a flat list
                // of top-level statements. Walk each one as a regular statement so any
                // identifiers it references (e.g., `new Hono()`) are picked up for
                // import emission.
                CollectFromStatement(topStmt.Inner, sink);
                break;
            case TsModuleExport:
                // `export default name;` / `export { name };` references a binding that
                // already exists locally — no external import to collect.
                break;
        }
    }

    private void CollectFromTypeParameters(
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

    private void CollectFromType(
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
            case TsFunctionType function:
                CollectFromType(function.ThisType, names, crossPackageOrigins);
                foreach (var p in function.Parameters)
                    CollectFromType(p.Type, names, crossPackageOrigins);
                CollectFromType(function.ReturnType, names, crossPackageOrigins);
                break;
            case TsTupleType tuple:
                foreach (var t in tuple.Elements)
                    CollectFromType(t, names, crossPackageOrigins);
                break;
            case TsTypePredicateType predicate:
                CollectFromType(predicate.Type, names, crossPackageOrigins);
                break;
            case TsObjectType obj:
                foreach (var field in obj.Fields)
                    CollectFromType(field.Type, names, crossPackageOrigins);
                break;
        }
    }

    private void CollectFromStatements(
        IReadOnlyList<TsStatement> statements,
        ImportCollectionSink sink
    )
    {
        foreach (var stmt in statements)
            CollectFromStatement(stmt, sink);
    }

    private void CollectFromStatement(TsStatement stmt, ImportCollectionSink sink)
    {
        var names = sink.Names;
        var valueNames = sink.ValueNames;
        var runtimeHelpers = sink.RuntimeHelpers;
        var crossPackageOrigins = sink.CrossPackageOrigins;
        switch (stmt)
        {
            case TsReturnStatement ret:
                if (ret.Expression is not null)
                    CollectFromExpression(ret.Expression, sink);
                break;
            case TsThrowStatement thr:
                CollectFromExpression(thr.Expression, sink);
                break;
            case TsExpressionStatement expr:
                CollectFromExpression(expr.Expression, sink);
                break;
            case TsIfStatement ifStmt:
                CollectFromExpression(ifStmt.Condition, sink);
                CollectFromStatements(ifStmt.Then, sink);
                if (ifStmt.Else is not null)
                    CollectFromStatements(ifStmt.Else, sink);
                break;
            case TsVariableDeclaration varDecl:
                CollectFromExpression(varDecl.Initializer, sink);
                break;
            case TsDestructuringDeclaration destructuring:
                // The destructured initializer can carry an [Import]-facade
                // template (e.g. `const [count, setCount] = createSignal(0)`),
                // so its external imports must be collected the same way a
                // plain variable initializer's are.
                CollectFromExpression(destructuring.Initializer, sink);
                break;
        }
    }

    private void CollectFromExpression(TsExpression expr, ImportCollectionSink sink)
    {
        var names = sink.Names;
        var valueNames = sink.ValueNames;
        var runtimeHelpers = sink.RuntimeHelpers;
        var crossPackageOrigins = sink.CrossPackageOrigins;
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
                // Method-group reference to a [NoContainer] static method lowers
                // to a bare lowercase identifier (delegate position, lambda
                // capture, callback arg). The resolution loop below routes the
                // name through `NoContainerFunctionExports` to emit the right
                // import line; without registering it here the loop never sees
                // it. (#179)
                else if (
                    bareId.Name.Length > 0
                    && _context.NoContainerFunctionExports.ContainsKey(bareId.Name)
                )
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
                    CollectFromExpression(arg, sink);
                break;
            case TsLinqCall linq:
                // Tree-shakeable LINQ pipe helpers — register every
                // helper the chain uses. The dedicated `linqPipeHelpers`
                // bucket emits a single named-import line from
                // `metano-runtime/system/linq`.
                foreach (var helper in linq.Helpers)
                    sink.LinqPipeHelpers.Add(helper);
                foreach (var arg in linq.Args)
                    CollectFromExpression(arg, sink);
                break;
            case TsCallExpression call:
                // Function calls may reference guard functions (e.g., isCurrency)
                if (call.Callee is TsIdentifier callId)
                {
                    names.Add(callId.Name);
                    valueNames.Add(callId.Name);
                    // `bindReceiver` is the metano-runtime helper the
                    // TS bridge emits around lambdas bound to a
                    // `[This]`-bearing delegate. Route it through the
                    // runtime-helper bucket so the collector emits
                    // the matching import without threading a new
                    // IrRuntimeRequirement through the frontend.
                    if (callId.Name == "bindReceiver")
                        runtimeHelpers.Add("bindReceiver");
                    // `getUnionGuard` / `registerUnionGuard` ship from
                    // `metano-runtime` and back the [StrictUnionGuard]
                    // dispatch. Route them through the runtime-helper
                    // bucket so the collector emits the matching
                    // import. The walker only sees calls (base guard
                    // looks up via `getUnionGuard(...)`; each variant
                    // module registers via `registerUnionGuard(...)`),
                    // so this is the right hook.
                    if (callId.Name is "getUnionGuard" or "registerUnionGuard")
                        runtimeHelpers.Add(callId.Name);
                }
                // Static method calls like Enumerable.from(...)
                else if (
                    call.Callee is TsPropertyAccess { Object: TsIdentifier { Name: var rootName } }
                    && char.IsUpper(rootName[0])
                )
                {
                    names.Add(rootName);
                    valueNames.Add(rootName);
                    CollectFromExpression(call.Callee, sink);
                }
                else
                {
                    CollectFromExpression(call.Callee, sink);
                }
                foreach (var arg in call.Arguments)
                    CollectFromExpression(arg, sink);
                break;
            case TsPropertyAccess access:
                CollectFromExpression(access.Object, sink);
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
                CollectFromExpression(bin.Left, sink);
                CollectFromExpression(bin.Right, sink);
                // instanceof uses the type as a value
                if (bin.Operator == "instanceof" && bin.Right is TsIdentifier instanceId)
                    valueNames.Add(instanceId.Name);
                break;
            case TsObjectLiteral obj:
                foreach (var prop in obj.Properties)
                    CollectFromExpression(prop.Value, sink);
                break;
            case TsTemplateLiteral tmpl:
                foreach (var e in tmpl.Expressions)
                    CollectFromExpression(e, sink);
                break;
            case TsConditionalExpression cond:
                CollectFromExpression(cond.Condition, sink);
                CollectFromExpression(cond.WhenTrue, sink);
                CollectFromExpression(cond.WhenFalse, sink);
                break;
            case TsJsxElement jsx:
                CollectFromJsxElement(jsx, sink);
                break;
            case TsAwaitExpression await_:
                CollectFromExpression(await_.Expression, sink);
                break;
            case TsParenthesized paren:
                CollectFromExpression(paren.Expression, sink);
                break;
            case TsSpreadExpression spread:
                CollectFromExpression(spread.Expression, sink);
                break;
            case TsArrowFunction arrow:
                foreach (var p in arrow.Parameters)
                    CollectFromType(p.Type, names, crossPackageOrigins);
                CollectFromStatements(arrow.Body, sink);
                break;
            case TsElementAccess elemAccess:
                CollectFromExpression(elemAccess.Object, sink);
                CollectFromExpression(elemAccess.Index, sink);
                break;
            case TsArrayLiteral arrayLit:
                foreach (var e in arrayLit.Elements)
                    CollectFromExpression(e, sink);
                break;
            case TsCastExpression cast:
                // Descend into both the inner expression AND the target type.
                // The target type can introduce a brand-new identifier that
                // appears nowhere else — e.g. `p.estimatedHours as Decimal` in a
                // generated serializer factory, where `Decimal` is referenced
                // only here and needs to end up in the import list. Without this
                // case, the walker would miss it entirely.
                CollectFromExpression(cast.Expression, sink);
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
                    CollectFromExpression(template.Receiver, sink);
                foreach (var arg in template.Arguments)
                    CollectFromExpression(arg, sink);
                // `$T0`/`$T1` placeholders embed generic type-argument names
                // verbatim into the template body — those names must be
                // resolved as referenced identifiers so the import collector
                // can pull in the matching declaration. PascalCase guard skips
                // primitive type aliases (`string`, `number`, `boolean`) that
                // don't need import lines.
                foreach (var typeArg in template.TypeArgumentNames)
                {
                    if (typeArg.Length == 0 || !char.IsUpper(typeArg[0]))
                        continue;
                    names.Add(typeArg);
                    valueNames.Add(typeArg);
                }
                // Runtime helper identifiers carried alongside the template (e.g.,
                // "dayNumber", "listRemove", "immutableInsert") from
                // [MapMethod(..., RuntimeImports = "...")] declarations. The walker can't
                // see these inside the opaque template text, so the BclMapper threads
                // them through here as a separate field. They go into the dedicated
                // `runtimeHelpers` set so the Collect entry point can emit a single
                // bundled `import { ... } from "metano-runtime"` line.
                foreach (var helper in template.RuntimeImports)
                    runtimeHelpers.Add(helper);
                // External `[Import]` metadata threaded from `[Emit]` methods —
                // the template body references the identifier by name (e.g.,
                // `createElement` in `createElement($T0, $0)`) but the walker
                // can't see it inside the opaque template text. The Collect
                // entry point drains this list into TsImport lines after the
                // walk finishes.
                foreach (var ext in template.ExternalImports)
                    sink.TemplateExternals.Add(ext);
                break;
        }
    }

    /// <summary>
    /// Walks a JSX element so component / helper names and any expressions
    /// embedded in attributes or children become referenced names. A
    /// capitalized <see cref="TsJsxElement.TagName"/> is a component (or a
    /// helper like <c>For</c>) used as a value — intrinsic lowercase tags
    /// (<c>div</c>, <c>span</c>) are JSX-runtime intrinsics and need no import.
    /// </summary>
    private void CollectFromJsxElement(TsJsxElement jsx, ImportCollectionSink sink)
    {
        // A tag backed by an explicit external import (e.g. the SolidJS `<For>`
        // helper) resolves through the external-import channel, not as a
        // transpilable component — so it must NOT be registered as a referenced
        // type name (that would synthesize a wrong intra-project import line).
        if (jsx.ExternalImports is { Count: > 0 } externalImports)
        {
            foreach (var ext in externalImports)
                sink.TemplateExternals.Add(ext);
        }
        else if (jsx.TagName.Length > 0 && char.IsUpper(jsx.TagName[0]))
        {
            sink.Names.Add(jsx.TagName);
            sink.ValueNames.Add(jsx.TagName);
        }
        foreach (var attribute in jsx.Attributes)
        {
            if (attribute.Value is TsJsxAttributeExpressionValue expr)
                CollectFromExpression(expr.Expression, sink);
        }
        foreach (var child in jsx.Children)
        {
            switch (child)
            {
                case TsJsxExpressionChild expr:
                    CollectFromExpression(expr.Expression, sink);
                    break;
                case TsJsxElementChild element:
                    CollectFromJsxElement(element.Element, sink);
                    break;
            }
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

/// <summary>
/// Bundle of collection buckets threaded through the <see cref="ImportCollector"/>
/// reference walker. Every walker method writes into the same instance; the entry
/// point (<see cref="ImportCollector.Collect"/>) drains the buckets after the walk
/// to build the file's import lines.
/// </summary>
internal sealed record ImportCollectionSink(
    HashSet<string> Names,
    HashSet<string> ValueNames,
    HashSet<string> RuntimeHelpers,
    Dictionary<string, TsTypeOrigin> CrossPackageOrigins,
    List<IrExternalImport> TemplateExternals,
    HashSet<string> LinqPipeHelpers
);
