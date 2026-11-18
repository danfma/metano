using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Metano.Compiler.TypeScript.Parenthesization.Templates;
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
    /// <summary>
    /// The module specifier the runtime helpers are imported from. The alias
    /// pass (issue #484) treats its lines (root barrel and subpaths) as the
    /// runtime's own claims; a helper name arriving on any other line is a
    /// user binding.
    /// </summary>
    private const string RuntimeModuleName = TsRuntimeHelperReference.RootModule;

    /// <summary>
    /// The polyfill specifier the <c>Temporal</c> binding imports from — a
    /// runtime line like the <c>metano-runtime</c> ones, so its names don't
    /// count as user claims.
    /// </summary>
    private const string TemporalModuleName = "@js-temporal/polyfill";

    /// <summary>The <c>metano-runtime</c> descriptor types a file imports as types only.</summary>
    private static readonly IReadOnlySet<string> RuntimeDescriptorTypeNames = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "ClassType",
        "InterfaceType",
        "EnumType",
        "RuntimeType",
    };

    private static readonly StringComparer Ordinal = StringComparer.Ordinal;

    private readonly TypeScriptTransformContext _context = context;
    private readonly TypeMappingContext _typeMappingContext = context.TypeMapping
        ?? throw new InvalidOperationException(
            "ImportCollector requires TypeScriptTransformContext.TypeMapping to be set."
        );
    private readonly IReadOnlySet<IrRuntimeRequirement>? _irRuntimeRequirements =
        irRuntimeRequirements;

    /// <summary>
    /// The emitted file being collected for, set at the start of
    /// <see cref="Collect"/> so the origin-clash diagnostics name it.
    /// </summary>
    private string _currentFileName = "";

    /// <summary>Names whose import clash MS0070 already reported, one per file.</summary>
    private readonly HashSet<string> _namesWithReportedClash = new(StringComparer.Ordinal);

    /// <summary>
    /// Identifiers that surface in generated TS but never need an
    /// <c>import</c> line: TypeScript built-ins (<c>Map</c>, <c>Set</c>,
    /// <c>unknown</c>, …), JavaScript globals (<c>console</c>,
    /// <c>Math</c>, <c>crypto</c>, <c>Object</c>), keywords used as
    /// expressions (<c>typeof</c>), and the event accessors
    /// (<c>delegateAdd</c>/<c>delegateRemove</c>) whose import the event
    /// lowering wires separately. Runtime helper calls are not listed here:
    /// they travel as <see cref="TsRuntimeHelperReference"/> nodes and the
    /// walker collects them through the dedicated helper bucket.
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
    };

    /// <summary>
    /// The module's import lines plus the <c>metano-runtime</c> helper aliases
    /// the file must print its helper calls through (empty when no helper name
    /// collides with a user binding or import — see
    /// <see cref="AliasCollidingRuntimeHelpers"/>).
    /// </summary>
    internal CollectedImports Collect(INamedTypeSymbol currentType, List<TsTopLevel> statements)
    {
        var referencedTypes = new HashSet<string>();
        var valueTypes = new HashSet<string>(); // types used via `new` or `extends` (need runtime import)
        var runtimeHelperReferences = new HashSet<string>(StringComparer.Ordinal); // value identifiers from TsRuntimeHelperReference
        var runtimeHelperTypeReferences = new HashSet<string>(StringComparer.Ordinal); // type bindings from TsNamedType.RuntimeHelperBinding
        var templateRuntimeHelpers = new HashSet<string>(StringComparer.Ordinal); // identifiers from TsTemplate.RuntimeImports
        var shadowingNames = new HashSet<string>(StringComparer.Ordinal); // every binding identifier anywhere in the file
        var crossPackageOrigins = new Dictionary<string, TsTypeOrigin>(); // name → cross-package origin
        var templateExternals = new List<IrExternalImport>();
        var linqPipeHelpers = new HashSet<string>(StringComparer.Ordinal);
        var ownersByModuleExport = new Dictionary<string, SortedSet<string>>(
            StringComparer.Ordinal
        );
        var sink = new ImportCollectionSink(
            referencedTypes,
            valueTypes,
            runtimeHelperReferences,
            runtimeHelperTypeReferences,
            templateRuntimeHelpers,
            shadowingNames,
            crossPackageOrigins,
            templateExternals,
            linqPipeHelpers,
            ownersByModuleExport
        );
        _currentFileName = GetFileName(currentType);
        CollectReferencedTypeNames(statements, sink);

        // C# `using X = Y;` aliases substitute the canonical type name with
        // the user's alias on every emitted token. The walker therefore sees
        // the alias text only; recover the canonical via the per-file scope
        // so the import line resolves to the canonical and emits the
        // matching `{ Canonical as Alias }` form.
        //
        // Every alias a file spells binds an import (`Widget as W1`, `Widget as
        // W2`), each with its own type-only flag. The canonical name binds plainly
        // only when the text names it too (a value position keeps the source
        // spelling), which `spelledCanonically` records before the substitution.
        var spelledCanonically = referencedTypes.ToHashSet(Ordinal);
        var aliasBindings = new Dictionary<string, List<(string Alias, bool IsValue)>>(Ordinal);

        if (IrToTsTypeMapper.UsingAliases is { } activeScope)
        {
            foreach (var name in referencedTypes.ToList())
            {
                if (!activeScope.AliasToCanonical.TryGetValue(name, out var canonical))
                    continue;

                referencedTypes.Add(canonical);

                if (!aliasBindings.TryGetValue(canonical, out var bindings))
                {
                    bindings = [];
                    aliasBindings[canonical] = bindings;
                }

                bindings.Add((name, valueTypes.Contains(name)));
                referencedTypes.Remove(name);
                valueTypes.Remove(name);
            }
        }

        var aliasedOrigins = RecoverAliasedOrigins(crossPackageOrigins, valueTypes);

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
        //
        // `runtimeImported` tracks the names the runtime itself claims. It is
        // kept apart from `importedNames` (the user-import dedupe): a user
        // import of the same name must still be emitted — the runtime one is
        // aliased instead of swallowing the user's binding (#484).
        var runtimeImported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var imp in imports)
        foreach (var name in imp.Names)
            runtimeImported.Add(name);

        // `Grouping<K,T>` is shipped from `metano-runtime/system/linq` (the pipe
        // runtime) and re-exported from the root `metano-runtime` barrel.
        // Pull it in as a type-only import when the consumer file references
        // it — from a signature's plain name or a helper-bound type — but
        // didn't go through the LINQ-pipe import path.
        if (
            (
                referencedTypes.Contains("Grouping")
                || sink.RuntimeHelperTypeReferences.Contains("Grouping")
            ) && !runtimeImported.Contains("Grouping")
        )
        {
            imports.Add(new TsImport(["Grouping"], RuntimeModuleName, TypeOnly: true));
            runtimeImported.Add("Grouping");
        }

        // The runtime type descriptor types (spec 011 FR-017) are types only:
        // a descriptor's annotation (`ClassType<X>`) and `System.Type`
        // (`RuntimeType`) import no code.
        var descriptorTypes = sink
            .RuntimeHelperTypeReferences.Where(RuntimeDescriptorTypeNames.Contains)
            .Where(name => !runtimeImported.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (descriptorTypes.Length > 0)
        {
            imports.Add(new TsImport(descriptorTypes, RuntimeModuleName, TypeOnly: true));
            runtimeImported.UnionWith(descriptorTypes);
        }

        var referencesUuid =
            referencedTypes.Contains("UUID")
            || sink.RuntimeHelperTypeReferences.Contains("UUID")
            || runtimeHelperReferences.Contains("UUID")
            || templateRuntimeHelpers.Contains("UUID");

        if (referencesUuid && !runtimeImported.Contains("UUID"))
        {
            // The dedicated UUID line and any helper-bucket line carrying UUID
            // merge into one import below (MergeImportsByPath dedupes by
            // name and alias), so the buckets stay untouched.
            imports.Add(new TsImport(["UUID"], RuntimeModuleName));
            runtimeImported.Add("UUID");
        }

        // Runtime helper calls collected from TsRuntimeHelperReference nodes —
        // the value references the printer routes through the file's helper
        // aliases. A name the IR scanner already imported is merged into the
        // same "metano-runtime" line by MergeImportsByPath, so no ExceptWith
        // here: the alias (issue #484) must survive the merge.
        if (runtimeHelperReferences.Count > 0)
        {
            imports.Add(
                new TsImport(runtimeHelperReferences.OrderBy(n => n).ToArray(), RuntimeModuleName)
            );
            runtimeImported.UnionWith(runtimeHelperReferences);
        }

        // Runtime helper imports collected from TsTemplate.RuntimeImports declarations
        // (e.g., dayNumber, listRemove, immutableInsert, immutableRemoveAt, immutableRemove).
        // Bundled into a single import line from metano-runtime. A helper the
        // IR scanner already value-imported (`HashSet` used both in a signature
        // and by a constructor-mapping template) must not be bound twice; a
        // type-only import does not provide the value, so it is kept.
        templateRuntimeHelpers.ExceptWith(ValueImportedNames(imports));

        if (templateRuntimeHelpers.Count > 0)
        {
            imports.Add(
                new TsImport(templateRuntimeHelpers.OrderBy(n => n).ToArray(), RuntimeModuleName)
            );
            runtimeImported.UnionWith(templateRuntimeHelpers);
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

        // Track the user-facing imports already emitted to avoid duplicates
        // downstream; runtime bindings are not here (see `runtimeImported`).
        var importedNames = new HashSet<string>(StringComparer.Ordinal);

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

        // An origin staged under a `using` alias imports its export under the
        // alias: `import { Decimal as D } from "decimal.js"`. The merge below
        // keeps it beside a plain import of the canonical name
        // (`import { Decimal, Decimal as D }`).
        foreach (var aliased in aliasedOrigins)
        {
            _typeMappingContext.ConfirmCrossPackageDependency(aliased.Origin.PackageName);

            var importPath =
                aliased.Origin.SubPath.Length > 0
                    ? $"{aliased.Origin.PackageName}/{aliased.Origin.SubPath}"
                    : aliased.Origin.PackageName;

            imports.Add(
                aliased.Origin.IsDefault
                    ? new TsImport([aliased.Alias], importPath, IsDefault: true)
                    : new TsImport(
                        [aliased.Canonical],
                        importPath,
                        TypeOnly: !aliased.IsValue,
                        Aliases: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [aliased.Canonical] = aliased.Alias,
                        }
                    )
            );
        }

        // Local imports (guards + transpilable types within the project) are bucketed
        // by import path so multiple names from the same barrel collapse into one line.
        // Same strategy the cross-package loop above uses — keeps barrel-first output
        // clean and Biome-friendly (e.g., all-type-only → whole-statement `import type`
        // rather than per-name `{ type A, type B }`).
        var localByPath = new Dictionary<string, ImportBindings>(StringComparer.Ordinal);

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
                bucket = new ImportBindings();
                localByPath[importPath] = bucket;
            }

            bucket.Add(name, alias: null, typeOnly);
        }

        // A project type: bound under every `using` alias the file spells for it, and
        // plainly only when the file also names it by its own name.
        void AddLocalType(string importPath, string canonical, string tsName, bool typeOnly)
        {
            if (!aliasBindings.TryGetValue(canonical, out var aliases))
            {
                AddLocal(importPath, tsName, typeOnly);

                return;
            }

            if (!localByPath.TryGetValue(importPath, out var bucket))
            {
                bucket = new ImportBindings();
                localByPath[importPath] = bucket;
            }

            foreach (var (alias, isValue) in aliases)
                bucket.Add(tsName, alias, typeOnly: !isValue);

            // A same-named [NoContainer] factory owns the plain spelling.
            if (
                spelledCanonically.Contains(canonical)
                && !_context.NoContainerFunctionExports.ContainsKey(canonical)
            )
                bucket.Add(tsName, alias: null, typeOnly);
        }

        void ImportErasableExport(string name, NoContainerExport erasableExport)
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

                AddLocal(crossPath, name, typeOnly: false);

                return;
            }

            // Same emit target — no import needed.
            var erasableOwner = erasableExport.Owner;

            if (erasableOwner.Namespace == currentNs && erasableOwner.FileName == currentFileName)
                return;

            var fnPath = _context.PathNaming.ComputeRelativeImportPath(
                currentNs,
                erasableOwner.Namespace,
                erasableOwner.FileName
            );

            AddLocal(fnPath, name, typeOnly: false);
        }

        var importedAliasedTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeName in referencedTypes.OrderBy(n => n))
        {
            // Skip built-in types and runtime identifiers that don't need imports.
            if (typeName.StartsWith("Temporal.") || NonImportableIdentifiers.Contains(typeName))
                continue;

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
                    _typeMappingContext.CrossPackageDependencies.Record(
                        extImport.From,
                        extImport.Version
                    );
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
            // to share the same emitted name. A name the text also spells as
            // itself is the factory's too, and the aliased type still binds
            // below.
            var namesAliasedType = aliasBindings.ContainsKey(typeName);

            if (namesAliasedType && !spelledCanonically.Contains(typeName))
            {
                // fall through to the transpilable-type branch
            }
            else if (
                _context.NoContainerFunctionExports.TryGetValue(typeName, out var erasableExport)
                && importedNames.Add(typeName)
            )
            {
                ImportErasableExport(typeName, erasableExport);

                if (!namesAliasedType)
                    continue;
            }

            // Transpilable type within the project
            if (!_context.TranspilableTypes.TryGetValue(typeName, out var referencedRef))
                continue;

            // An origin-carrying import (a BCL export or a cross-package
            // type) and a project type of the same emitted name collide on
            // the one identifier: both import lines bind it, and every bare
            // reference is ambiguous. Loud diagnostic instead of the silent
            // single-module binding the name match produced (issue #477).
            // Even a co-located [EmitInFile] type collides — the bare name
            // binds the local declaration too.
            if (
                crossPackageOrigins.TryGetValue(typeName, out var collidingOrigin)
                && _namesWithReportedClash.Add(typeName)
            )
                ReportImportedNameClash(
                    typeName,
                    $"an import from '{collidingOrigin.PackageName}'",
                    "a project type"
                );

            // Skip types co-located in the same file via [EmitInFile] — they're
            // declared locally in the merged source, no import needed.
            if (referencedRef.Namespace == currentNs && referencedRef.FileName == currentFileName)
                continue;

            // The aliased type binds apart from a same-named factory imported above.
            var alreadyImported = namesAliasedType
                ? !importedAliasedTypes.Add(typeName)
                : !importedNames.Add(typeName);

            if (alreadyImported)
                continue;

            // Path is computed against the FILE name (not the type name) so multiple
            // types co-located in the same file resolve to the same import path.
            var importPath = _context.PathNaming.ComputeRelativeImportPath(
                currentNs,
                referencedRef.Namespace,
                referencedRef.FileName
            );
            var typeOnly = !valueTypes.Contains(typeName);
            AddLocalType(importPath, typeName, referencedRef.TsName, typeOnly);
        }

        AddModuleExportImports(
            ownersByModuleExport,
            currentNs,
            currentFileName,
            importedNames,
            AddLocal
        );

        // Emit one merged TsImport per bucketed local path (see ImportBindings.ToImport):
        //   - all type-only → `import type { A, B } from "..."`       (whole statement)
        //   - all value     → `import { A, B } from "..."`            (plain)
        //   - mixed         → `import { A, type B } from "..."`       (per-name)
        // The whole-statement form is preferred when everything is type-only so Biome's
        // `noImportTypeQualifier` lint stays quiet.
        foreach (
            var (importPath, bucket) in localByPath.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        )
            imports.Add(bucket.ToImport(importPath));

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
                    _typeMappingContext.CrossPackageDependencies.Record(ext.From, version);
            }
        }

        var (stampedImports, helperAliases) = AliasCollidingRuntimeHelpers(
            imports,
            statements,
            sink
        );

        return new CollectedImports(MergeImportsByPath(stampedImports), helperAliases);
    }

    /// <summary>
    /// A <c>metano-runtime</c> helper whose plain import name is already taken
    /// by a user declaration, parameter, local, or user import anywhere in the
    /// module is re-imported under a unique <c>$rt</c> alias, and the file's
    /// helper calls print through that alias (issue #484). Without it the
    /// module either fails to compile (duplicate identifier) or the generated
    /// calls silently bind to the user's member.
    ///
    /// Aliased channels: the walker's <see cref="TsRuntimeHelperReference"/>
    /// calls, the type-position bindings of class-shaped helpers
    /// (<c>HashSet&lt;T&gt;</c>, <c>UUID</c>, <c>Grouping&lt;K, V&gt;</c>),
    /// the names the BCL-mapping templates declare in <c>RuntimeImports</c>
    /// (the printer substitutes the alias into the template's literal
    /// chunks), and the LINQ pipe operators. <c>Temporal</c> is the one
    /// exception: its only value-side emission is a mapping template that
    /// doesn't declare the binding, so an alias would break that call while
    /// the type references alias — a collision reports MS0071 instead.
    /// </summary>
    private (
        List<TsImport> Imports,
        IReadOnlyDictionary<string, string> Aliases
    ) AliasCollidingRuntimeHelpers(
        List<TsImport> imports,
        List<TsTopLevel> statements,
        ImportCollectionSink sink
    )
    {
        var claimed = NamesClaimedByRuntime(sink);
        var occupied = OccupiedModuleNames(statements, imports, claimed, sink);
        ReportUnaliasableHelperCollisions(claimed, occupied);
        var aliasable = AliasableHelperNames(sink);
        var aliases = AllocateHelperAliases(aliasable, occupied);
        var stamped = WithRuntimeAliases(imports, aliases);

        return (stamped, aliases);
    }

    /// <summary>
    /// Every name the runtime itself claims in this module: the walker's
    /// helper references (value and type position), the template-carried
    /// helpers, the LINQ pipe helpers, and the imports the IR
    /// runtime-requirement scanner asked for (semantic name → emitted name).
    /// Their own import lines are excluded from the occupied set — they are
    /// the candidates being aliased, not collisions.
    /// </summary>
    private HashSet<string> NamesClaimedByRuntime(ImportCollectionSink sink)
    {
        // The aliasable channels are the runtime's claims; the IR-scanner
        // requirements add the names a mapped import declares.
        var claimed = AliasableHelperNames(sink);

        if (_irRuntimeRequirements is { Count: > 0 } requirements)
        {
            foreach (var requirement in requirements)
            {
                if (IrRuntimeRequirementToTsImport.HasMappedImport(requirement))
                    claimed.Add(
                        IrRuntimeRequirementToTsImport.ImportedNameOf(requirement.HelperName)
                    );
            }
        }

        return claimed;
    }

    /// <summary>
    /// Whether the import line belongs to a runtime module (the root barrel
    /// or one of its subpaths); only those lines carry the runtime's own
    /// claims — a user <c>[Import]</c> from <c>metano-runtime</c> of a name
    /// the runtime doesn't claim stays a user binding.
    /// </summary>
    private static bool IsRuntimeNamedImportLine(TsImport import) =>
        import.From == RuntimeModuleName
        || import.From.StartsWith($"{RuntimeModuleName}/")
        || import.From == TemporalModuleName;

    /// <summary>
    /// Every name that would shadow or clash with a helper binding: the
    /// module-scope declarations, every parameter / local / destructuring
    /// binding anywhere in the file (a generated call inside that scope would
    /// bind to it), and every imported binding that is not the runtime's own
    /// helper import. The one gap is a catch variable: try/catch prints as
    /// raw text, so its binding name is not collected (the bodies still are).
    /// Type-only imports occupy module scope too (a local type
    /// of the same name is a duplicate identifier either way). A name the
    /// runtime claims counts as occupied when it arrives on a USER import
    /// line — the user's binding wins and the runtime one is what gets
    /// aliased (#484).
    /// </summary>
    private static HashSet<string> OccupiedModuleNames(
        IReadOnlyList<TsTopLevel> statements,
        IReadOnlyList<TsImport> imports,
        IReadOnlySet<string> claimed,
        ImportCollectionSink sink
    )
    {
        var occupied = new HashSet<string>(ModuleScopeDeclaredNames(statements), Ordinal);

        occupied.UnionWith(sink.ShadowingNames);

        foreach (var import in imports)
        {
            if (import.IsNamespace)
                continue;

            var runtimeLine = IsRuntimeNamedImportLine(import);

            foreach (var name in import.Names)
            {
                if (runtimeLine && claimed.Contains(name))
                    continue;

                occupied.Add(name);
            }
        }

        return occupied;
    }

    /// <summary>
    /// The helper names whose references the printer reroutes end to end:
    /// walker helper calls (value), class-shaped helper type bindings,
    /// template-declared helpers, and the LINQ pipe operators.
    /// </summary>
    private static HashSet<string> AliasableHelperNames(ImportCollectionSink sink)
    {
        var aliasable = new HashSet<string>(StringComparer.Ordinal);

        aliasable.UnionWith(sink.RuntimeHelperReferences);
        aliasable.UnionWith(sink.RuntimeHelperTypeReferences);
        aliasable.UnionWith(sink.TemplateRuntimeHelpers);
        aliasable.UnionWith(sink.LinqPipeHelpers);

        return aliasable;
    }

    /// <summary>
    /// The one helper the printer cannot reroute end to end: <c>Temporal</c>'s
    /// only value-side emission (<c>DateTimeOffset.UtcNow</c>) is a mapping
    /// template that doesn't declare the binding, so an alias would break
    /// that call while the type references alias.
    /// </summary>
    private static readonly IReadOnlySet<string> UnaliasableHelpers = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "Temporal",
    };

    /// <summary>
    /// Reports MS0071 when a user name takes an unaliasable runtime helper:
    /// the module would emit a duplicate identifier, and there is no alias
    /// path to repair it — the user renames via <c>[Name]</c>.
    /// </summary>
    private void ReportUnaliasableHelperCollisions(
        IReadOnlySet<string> claimed,
        IReadOnlySet<string> occupied
    )
    {
        foreach (var helper in UnaliasableHelpers.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (claimed.Contains(helper) && occupied.Contains(helper))
            {
                _context.ReportDiagnostic(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.RuntimeHelperNameHijacked,
                        $"'{helper}' is a runtime type the generated module imports, and a user "
                            + "declaration or import in the same module takes that name. The "
                            + "collision cannot be aliased; rename the user member with "
                            + "[Name(\"...\")] (or the import via [Import(name: \"...\")])."
                    )
                );
            }
        }
    }

    /// <summary>
    /// Picks a unique alias per colliding helper: <c>name$rt</c>, then
    /// <c>name$rt2</c>, … until one is free among the occupied names and the
    /// aliases already handed out.
    /// </summary>
    private static IReadOnlyDictionary<string, string> AllocateHelperAliases(
        IReadOnlySet<string> aliasable,
        IReadOnlySet<string> occupied
    )
    {
        var aliases = new Dictionary<string, string>(Ordinal);
        var reservedNames = new HashSet<string>(occupied, Ordinal);

        foreach (var helper in aliasable.OrderBy(n => n, Ordinal))
        {
            if (!occupied.Contains(helper))
                continue;

            aliases[helper] = UniqueAlias(helper, reservedNames);
        }

        return aliases;
    }

    private static string UniqueAlias(string helper, ISet<string> reservedNames)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = attempt is 1 ? $"{helper}$rt" : $"{helper}$rt{attempt}";

            if (reservedNames.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Stamps the runtime import lines with the aliases: only
    /// <see cref="IsRuntimeNamedImportLine"/> lines — a user import of the
    /// same name keeps its binding untouched.
    /// </summary>
    private static List<TsImport> WithRuntimeAliases(
        List<TsImport> imports,
        IReadOnlyDictionary<string, string> aliases
    )
    {
        if (aliases.Count is 0)
            return imports;

        var stamped = new List<TsImport>(imports.Count);

        foreach (var import in imports)
        {
            var isStampable =
                IsRuntimeNamedImportLine(import) && !import.IsDefault && !import.IsNamespace;
            var aliased = isStampable
                ? import
                    .Names.Where(aliases.ContainsKey)
                    .ToDictionary(name => name, name => aliases[name], Ordinal)
                : [];

            if (aliased.Count is 0)
            {
                stamped.Add(import);
                continue;
            }

            if (import.Aliases is not null)
            {
                foreach (var (original, local) in import.Aliases)
                    if (!aliased.ContainsKey(original))
                        aliased[original] = local;
            }

            stamped.Add(import with { Aliases = aliased });
        }

        return stamped;
    }

    /// <summary>
    /// Every binding a generated module declares at file scope: classes,
    /// interfaces, functions, enums, const objects, type aliases, namespace
    /// declarations, barrel <c>export import</c> aliases, and the top-level
    /// (entry-point) variable and destructuring bindings. Namespaces' inner
    /// members are scoped to the namespace and don't occupy module scope.
    /// </summary>
    private static IReadOnlySet<string> ModuleScopeDeclaredNames(
        IReadOnlyList<TsTopLevel> statements
    )
    {
        var names = new HashSet<string>(Ordinal);

        foreach (var statement in statements)
            names.UnionWith(TopLevelBindingNames(statement));

        return names;
    }

    private static IEnumerable<string> TopLevelBindingNames(TsTopLevel statement) =>
        statement switch
        {
            TsFunction function => [function.Name],
            TsClass cls => [cls.Name],
            TsInterface iface => [iface.Name],
            TsConstObject constObject => [constObject.Name],
            TsTypeAlias typeAlias => [typeAlias.Name],
            TsNamespaceDeclaration namespaceDeclaration => [namespaceDeclaration.Name],
            TsExportImportAlias exportImport => [exportImport.Alias],
            TsTopLevelStatement topLevel when topLevel.Inner is TsVariableDeclaration variable =>
            [
                variable.Name,
            ],
            TsTopLevelStatement topLevel
                when topLevel.Inner is TsDestructuringDeclaration destructuring =>
                destructuring.Names.OfType<string>(),
            _ => [],
        };

    /// <summary>
    /// Imports each module export reference (a <c>[PlainObject]</c> instance
    /// method's helper, or a containerless static class's module variable)
    /// from its owner type's file. A file that reads the same name from two
    /// owners cannot bind both under one name, so that reports MS0021 instead
    /// of silently reading one owner's export for the other; a <c>[Name]</c>
    /// override on either member resolves it.
    /// </summary>
    private void AddModuleExportImports(
        IReadOnlyDictionary<string, SortedSet<string>> ownersByModuleExport,
        string currentNs,
        string currentFileName,
        HashSet<string> importedNames,
        Action<string, string, bool> addLocal
    )
    {
        foreach (var (exportName, owners) in ownersByModuleExport.OrderBy(kv => kv.Key))
        {
            if (owners.Count > 1)
                ReportModuleExportClash(exportName, owners);

            if (!_context.TranspilableTypes.TryGetValue(owners.Min!, out var ownerRef))
                continue;

            var isCurrentFile =
                ownerRef.Namespace == currentNs && ownerRef.FileName == currentFileName;

            if (isCurrentFile || !importedNames.Add(exportName))
                continue;

            var ownerPath = _context.PathNaming.ComputeRelativeImportPath(
                currentNs,
                ownerRef.Namespace,
                ownerRef.FileName
            );

            addLocal(ownerPath, exportName, false);
        }
    }

    private void ReportModuleExportClash(string exportName, IEnumerable<string> owners) =>
        _context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.ExtensionHelperNameClash,
                $"'{exportName}' is read from {string.Join(" and ", owners)} in one file, "
                    + "but each exports its own member of that name. Add a [Name(\"...\")] "
                    + "override on one of the members so both can be imported."
            )
        );

    /// <summary>
    /// MS0070: one emitted name binds two different imports — two origins
    /// staged for the same identifier, or an origin and a project type of
    /// the same name. The bare references cannot tell the two bindings
    /// apart and one of them is wrong at runtime. Rename one of the two
    /// with <c>[Name]</c> (or the BCL export's <c>ExportedName</c>); the
    /// alias emission for both cases is #504 (issue #477).
    /// </summary>
    private void ReportImportedNameClash(string name, string firstBinding, string secondBinding) =>
        _context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.ImportedNameClash,
                $"'{name}' names both {firstBinding} and {secondBinding} in '{_currentFileName}'; "
                    + "the bare name is ambiguous. Rename one of the two with [Name(\"...\")]."
            )
        );

    /// <summary>
    /// Takes out of <paramref name="crossPackageOrigins"/> every origin the
    /// walker staged under a <c>using</c> alias (the printed name), so it is
    /// imported under its exported, canonical name with the alias as the local
    /// binding. Each one keeps whether a value position referenced it.
    /// </summary>
    private static List<AliasedOrigin> RecoverAliasedOrigins(
        Dictionary<string, TsTypeOrigin> crossPackageOrigins,
        HashSet<string> valueTypes
    )
    {
        if (IrToTsTypeMapper.UsingAliases is not { } scope)
            return [];

        var aliased = new List<AliasedOrigin>();

        foreach (var (name, origin) in crossPackageOrigins.ToList())
        {
            if (!scope.AliasToCanonical.TryGetValue(name, out var canonical))
                continue;

            crossPackageOrigins.Remove(name);
            aliased.Add(new AliasedOrigin(canonical, name, origin, valueTypes.Remove(name)));
        }

        return aliased;
    }

    private sealed record AliasedOrigin(
        string Canonical,
        string Alias,
        TsTypeOrigin Origin,
        bool IsValue
    );

    /// <summary>
    /// Stages <paramref name="origin"/> for <paramref name="name"/>, reporting
    /// MS0070 when the name already stages a different package or sub-path:
    /// both import lines would bind the one identifier. The first origin
    /// staged keeps the binding (issue #477).
    /// </summary>
    private void StageOrigin(
        Dictionary<string, TsTypeOrigin> crossPackageOrigins,
        string name,
        TsTypeOrigin origin
    )
    {
        if (crossPackageOrigins.TryGetValue(name, out var existing))
        {
            if (
                (existing.PackageName != origin.PackageName || existing.SubPath != origin.SubPath)
                && _namesWithReportedClash.Add(name)
            )
                ReportImportedNameClash(
                    name,
                    $"an import from '{existing.PackageName}'",
                    $"an import from '{origin.PackageName}'"
                );

            return;
        }

        crossPackageOrigins[name] = origin;
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

            var bindings = new ImportBindings();

            foreach (var item in items)
                bindings.Add(item);

            merged.Add(bindings.ToImport(group.Key.From));
        }

        return merged;
    }

    /// <summary>
    /// Sorts named-import entries with values first (alphabetical), then type-only
    /// names (alphabetical). In all-value or all-type-only buckets this reduces to a
    /// plain alphabetical sort. In mixed buckets it produces the more readable
    /// <c>{ Value, type Type }</c> shape instead of interleaving them by name.
    /// </summary>
    internal static void SortValuesFirst(List<string> names, IReadOnlySet<string> typeOnlyNames)
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
        var valueNames = sink.ValueNames;

        switch (node)
        {
            case TsTypeAlias alias:
                CollectFromTypeParameters(alias.TypeParameters, sink);
                CollectFromType(alias.Type, sink);
                break;
            case TsInterface iface:
                CollectFromTypeParameters(iface.TypeParameters, sink);
                if (iface.Extends is not null)
                    foreach (var baseRef in iface.Extends)
                        CollectFromType(baseRef, sink);
                foreach (var prop in iface.Properties)
                    CollectFromType(prop.Type, sink);
                if (iface.Methods is not null)
                    foreach (var method in iface.Methods)
                    {
                        CollectFromTypeParameters(method.TypeParameters, sink);
                        foreach (var p in method.Parameters)
                            CollectFromType(p.Type, sink);
                        CollectFromType(method.ReturnType, sink);
                    }
                break;
            case TsFunction func:
                CollectFromTypeParameters(func.TypeParameters, sink);

                CollectFromParameters(Signature(func.Parameters), sink, () =>
                {
                    CollectFromType(func.ReturnType, sink);
                    CollectFromStatements(func.Body, sink);
                });

                break;
            case TsConstObject constObj:
                foreach (var entry in constObj.Entries)
                    CollectFromExpression(entry.Value, sink);

                CollectFromType(constObj.Shape, sink);

                break;
            case TsNamespaceDeclaration ns:
                // Companion namespaces hold both branded-companion functions and nested
                // type companions (variant classes) in Members. Both must be walked so
                // symbols referenced only inside the block — external types, value refs,
                // guards — are imported. Recurse through the same entry point the top
                // level uses so every node kind is collected identically at any depth.
                foreach (var func in ns.Functions)
                    CollectFromTopLevel(func, sink);

                if (ns.Members is not null)
                    foreach (var member in ns.Members)
                        CollectFromTopLevel(member, sink);
                break;
            case TsClass cls:
                CollectFromTypeParameters(cls.TypeParameters, sink);

                if (cls.Extends is not null)
                {
                    CollectFromType(cls.Extends, sink);

                    if (cls.Extends is TsNamedType extendsNamed)
                        valueNames.Add(extendsNamed.Name);
                }

                if (cls.Implements is not null)
                    foreach (var iface in cls.Implements)
                        CollectFromType(iface, sink);

                if (cls.Constructor is not null)
                {
                    CollectFromParameters(Signature(cls.Constructor.Parameters), sink, () =>
                        CollectFromStatements(cls.Constructor.Body, sink)
                    );
                }

                foreach (var member in cls.Members)
                {
                    switch (member)
                    {
                        case TsMethodMember m:
                            CollectFromTypeParameters(m.TypeParameters, sink);

                            CollectFromParameters(Signature(m.Parameters), sink, () =>
                            {
                                CollectFromType(m.ReturnType, sink);
                                CollectFromStatements(m.Body, sink);
                            });

                            // Collect from overload signatures too
                            if (m.Overloads is not null)
                                foreach (var overload in m.Overloads)
                                {
                                    foreach (var p in overload.Parameters)
                                        CollectFromType(p.Type, sink);
                                    CollectFromType(overload.ReturnType, sink);
                                }
                            break;
                        case TsGetterMember g:
                            CollectFromType(g.ReturnType, sink);
                            CollectFromStatements(g.Body, sink);
                            break;
                        case TsFieldMember f:
                            CollectFromType(f.Type, sink);
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

    /// <summary>
    /// A parameter's annotation and its default value: a default such as
    /// <c>Color.Green</c> or <c>new Decimal("1.5")</c> references a value that
    /// needs a value import.
    /// </summary>
    private void CollectFromParameter(
        TsType? type,
        TsExpression? defaultValue,
        ImportCollectionSink sink
    )
    {
        CollectFromType(type, sink);

        if (defaultValue is not null)
            CollectFromExpression(defaultValue, sink);
    }

    private void CollectFromTypeParameters(
        IReadOnlyList<TsTypeParameter>? typeParams,
        ImportCollectionSink sink
    )
    {
        if (typeParams is null)
            return;
        foreach (var tp in typeParams)
        {
            if (tp.Constraint is not null)
                CollectFromType(tp.Constraint, sink);
        }
    }

    private void CollectFromType(TsType? type, ImportCollectionSink sink)
    {
        var names = sink.Names;
        var crossPackageOrigins = sink.CrossPackageOrigins;

        if (type is null)
            return;
        switch (type)
        {
            case TsNamedType named when named.RuntimeHelperBinding is { } binding:
                // A class-shaped runtime helper in type position (HashSet<T>,
                // UUID, Grouping<K, V>): the binding travels through the
                // type-reference bucket so the alias pass sees it alongside
                // the value references (#484). Type arguments still resolve
                // like any other type.
                sink.RuntimeHelperTypeReferences.Add(binding);
                if (named.TypeArguments is not null)
                    foreach (var arg in named.TypeArguments)
                        CollectFromType(arg, sink);
                break;
            case TsNamedType named:
                // If this type came from a cross-package source, register its origin
                // and DON'T add the simple name to `names` — we don't want the string
                // resolution loop to try (and fail) to find a local type by that name.
                if (named.Origin is not null)
                {
                    StageOrigin(crossPackageOrigins, RootSegment(named.Name), named.Origin);
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
                        CollectFromType(arg, sink);
                break;
            case TsArrayType array:
                CollectFromType(array.ElementType, sink);
                break;
            case TsPromiseType promise:
                CollectFromType(promise.Inner, sink);
                break;
            case TsUnionType union:
                foreach (var t in union.Types)
                    CollectFromType(t, sink);
                break;
            case TsIntersectionType intersection:
                foreach (var t in intersection.Types)
                    CollectFromType(t, sink);
                break;
            case TsFunctionType function:
                CollectFromType(function.ThisType, sink);
                foreach (var p in function.Parameters)
                    CollectFromType(p.Type, sink);
                CollectFromType(function.ReturnType, sink);
                break;
            case TsTupleType tuple:
                foreach (var t in tuple.Elements)
                    CollectFromType(t, sink);
                break;
            case TsTypePredicateType predicate:
                CollectFromType(predicate.Type, sink);
                break;
            case TsObjectType obj:
                foreach (var field in obj.Fields)
                    CollectFromType(field.Type, sink);
                break;
        }
    }

    private void CollectFromStatements(
        IReadOnlyList<TsStatement> statements,
        ImportCollectionSink sink
    )
    {
        sink.WithBoundNames(statements.SelectMany(DeclaredNames), () =>
        {
            foreach (var stmt in statements)
                CollectFromStatement(stmt, sink);
        });
    }

    /// <summary>
    /// The names a statement declares in its own list (a block scopes them over the
    /// whole list, so a use before the declaration is the local too).
    /// </summary>
    private static IEnumerable<string> DeclaredNames(TsStatement statement) =>
        statement switch
        {
            TsVariableDeclaration declaration => [declaration.Name],
            TsDestructuringDeclaration destructuring => destructuring.Names.OfType<string>(),
            _ => [],
        };

    /// <summary>
    /// Walks a function's parameters and, through <paramref name="walkBody"/>, its body,
    /// with the parameter names bound for both: a default value or a body that reads a
    /// parameter reads the local, never an export of that name.
    /// </summary>
    private void CollectFromParameters(
        IReadOnlyList<(string Name, TsType? Type, TsExpression? DefaultValue)> parameters,
        ImportCollectionSink sink,
        Action walkBody
    )
    {
        foreach (var parameter in parameters)
            sink.ShadowingNames.Add(parameter.Name);

        sink.WithBoundNames(parameters.Select(parameter => parameter.Name), () =>
        {
            foreach (var parameter in parameters)
                CollectFromParameter(parameter.Type, parameter.DefaultValue, sink);

            walkBody();
        });
    }

    private static IReadOnlyList<(string, TsType?, TsExpression?)> Signature(
        IEnumerable<TsParameter> parameters
    ) => [.. parameters.Select(p => (p.Name, p.Type, p.DefaultValue))];

    private static IReadOnlyList<(string, TsType?, TsExpression?)> Signature(
        IEnumerable<TsConstructorParam> parameters
    ) => [.. parameters.Select(p => (p.Name, (TsType?)p.Type, p.DefaultValue))];

    private void CollectFromStatement(TsStatement stmt, ImportCollectionSink sink)
    {
        var names = sink.Names;

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
            case TsBlockStatement block:
                CollectFromStatements(block.Statements, sink);
                break;
            case TsSwitchStatement switchStmt:
                CollectFromExpression(switchStmt.Discriminant, sink);

                foreach (var switchCase in switchStmt.Cases)
                {
                    if (switchCase.Test is not null)
                        CollectFromExpression(switchCase.Test, sink);

                    CollectFromStatements(switchCase.Body, sink);
                }

                break;
            case TsVariableDeclaration varDecl:
                sink.ShadowingNames.Add(varDecl.Name);

                if (varDecl.Type is not null)
                    CollectFromType(varDecl.Type, sink);

                if (varDecl.Initializer is not null)
                    CollectFromExpression(varDecl.Initializer, sink);

                break;
            case TsInlineCodeStatement inlineCode:
                // The inline text is opaque (spec 011 FR-032): only its holes
                // reference names.
                foreach (var hole in inlineCode.Holes)
                    CollectFromExpression(hole, sink);

                break;
            case TsTryStatement tryStmt:
                CollectFromStatements(tryStmt.Body, sink);

                if (tryStmt.Catch is { } handler)
                    CollectInScope([handler.Parameter], handler.Body, [], sink);

                if (tryStmt.Finally is { } finallyBody)
                    CollectFromStatements(finallyBody, sink);

                break;
            case TsRawStatement raw:
                // Loops print as raw text; walk the nodes the
                // text was rendered from so their references are imported.
                CollectFromStatements(raw.EmbeddedStatements, sink);

                foreach (var embedded in raw.EmbeddedExpressions)
                    CollectFromExpression(embedded, sink);

                foreach (var scope in raw.EmbeddedScopes)
                    CollectFromEmbeddedScope(scope, sink);

                break;
            case TsDestructuringDeclaration destructuring:
                // The destructured initializer can carry an [Import]-facade
                // template (e.g. `const [count, setCount] = createSignal(0)`),
                // so its external imports must be collected the same way a
                // plain variable initializer's are.
                foreach (var element in destructuring.Names)
                {
                    if (element is not null)
                        sink.ShadowingNames.Add(element);
                }
                CollectFromExpression(destructuring.Initializer, sink);
                break;
        }
    }

    /// <summary>
    /// Whether a bare identifier read for a member (<c>Name.member</c>) names a type used
    /// as a value: spelled like a type (PascalCase) or a project type emitted under a
    /// <c>[Name]</c> override of another spelling (<c>e$</c>).
    /// </summary>
    private bool NamesTypeValue(string name) =>
        name.Length > 0 && (char.IsUpper(name[0]) || _context.TranspilableTypes.ContainsKey(name));

    private void CollectFromEmbeddedScope(TsEmbeddedScope scope, ImportCollectionSink sink) =>
        CollectInScope(scope.BoundNames, scope.Statements, scope.Expressions, sink);

    /// <summary>
    /// Walks <paramref name="statements"/> and <paramref name="expressions"/> with
    /// <paramref name="boundNames"/> bound as locals (a <c>catch</c> parameter, a
    /// <c>foreach</c> variable), so a local never resolves to an export of another module.
    /// </summary>
    private void CollectInScope(
        IReadOnlyList<string> boundNames,
        IReadOnlyList<TsStatement> statements,
        IReadOnlyList<TsExpression> expressions,
        ImportCollectionSink sink
    )
    {
        foreach (var name in boundNames)
            sink.ShadowingNames.Add(name);

        sink.WithBoundNames(boundNames, () =>
        {
            CollectFromStatements(statements, sink);

            foreach (var expression in expressions)
                CollectFromExpression(expression, sink);
        });
    }

    private void CollectFromExpression(TsExpression expr, ImportCollectionSink sink)
    {
        var names = sink.Names;
        var valueNames = sink.ValueNames;
        var templateRuntimeHelpers = sink.TemplateRuntimeHelpers;
        var crossPackageOrigins = sink.CrossPackageOrigins;

        switch (expr)
        {
            case TsTypeReference typeRef:
                // Cross-package type used in expression position (`new EntityId(…)`,
                // `EpisodeKind.Feature`, `x instanceof Circle`). The wrapper carries the
                // origin so we can register the import without going through name-based
                // resolution. A value position needs the runtime binding, never a
                // type-only import.
                var typeRefRoot = RootSegment(typeRef.Name);
                StageOrigin(crossPackageOrigins, typeRefRoot, typeRef.Origin);
                valueNames.Add(typeRefRoot);
                break;
            case TsModuleExportReference exportReference:
                RecordModuleExport(sink.OwnersByModuleExport, exportReference);
                break;
            case TsRuntimeHelperReference helper:
                if (helper.Module is TsRuntimeHelperReference.LinqPipeModule)
                {
                    sink.LinqPipeHelpers.Add(helper.Name);
                }
                else
                {
                    sink.RuntimeHelperReferences.Add(helper.Name);
                }

                break;
            case TsIdentifier bareId when sink.IsLocallyBound(bareId.Name):
                // A parameter, local, loop variable or caught value: never an export.
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
                if (newExpr.Callee is TsIdentifier localId && sink.IsLocallyBound(localId.Name))
                {
                    // A constructor held in a local: nothing to import.
                }
                else if (newExpr.Callee is TsIdentifier id)
                {
                    names.Add(id.Name);
                    valueNames.Add(id.Name); // used as value (constructor call)
                    // For nested types like "Outer.Inner", also mark the root as value
                    if (id.Name.Contains('.'))
                    {
                        var rootName = RootSegment(id.Name);
                        names.Add(rootName);
                        valueNames.Add(rootName);
                    }
                }
                else
                {
                    CollectFromExpression(newExpr.Callee, sink);
                }

                foreach (var typeArgument in newExpr.TypeArguments ?? [])
                    CollectFromType(typeArgument, sink);

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
                if (
                    call.Callee is TsIdentifier localCallee
                    && sink.IsLocallyBound(localCallee.Name)
                )
                {
                    // A call through a local: nothing to import.
                }
                else if (call.Callee is TsIdentifier callId)
                {
                    names.Add(callId.Name);
                    valueNames.Add(callId.Name);
                    // `bindReceiver` reaches the output as a bare-identifier
                    // call: the frontend wraps method-group → `[This]`
                    // delegate conversions in an `IrCallExpression` over a
                    // plain `IrIdentifier` (IrExpressionExtractor), and the
                    // TS bridge maps lambdas bound to such delegates onto the
                    // same shape. Route it through the runtime-helper bucket
                    // so the collector emits the matching import — aliased on
                    // a name collision (#484).
                    if (callId.Name == "bindReceiver")
                        sink.RuntimeHelperReferences.Add("bindReceiver");
                }
                // Static method calls like Enumerable.from(...)
                else if (
                    call.Callee is TsPropertyAccess { Object: TsIdentifier { Name: var rootName } }
                    && NamesTypeValue(rootName)
                    && !sink.IsLocallyBound(rootName)
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

                foreach (var typeArgument in call.TypeArguments ?? [])
                    CollectFromType(typeArgument, sink);

                foreach (var arg in call.Arguments)
                    CollectFromExpression(arg, sink);
                break;
            case TsPropertyAccess access:
                CollectFromExpression(access.Object, sink);
                // Static member access like IssuePriority.High → collect the type as value
                if (
                    access.Object is TsIdentifier { Name: var propObjName }
                    && NamesTypeValue(propObjName)
                    && !sink.IsLocallyBound(propObjName)
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
            case TsInlineCode inlineCode:
                // The inline text is opaque (spec 011 FR-032): only its holes
                // reference names.
                foreach (var hole in TsInlineCodeHoles.Of(inlineCode.Parts))
                    CollectFromExpression(hole, sink);

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
            case TsUnaryExpression unary:
                CollectFromExpression(unary.Operand, sink);
                break;
            case TsPostfixUnaryExpression postfix:
                CollectFromExpression(postfix.Operand, sink);
                break;
            case TsSpreadExpression spread:
                CollectFromExpression(spread.Expression, sink);
                break;
            case TsArrowFunction arrow:
                foreach (var p in arrow.Parameters)
                {
                    sink.ShadowingNames.Add(p.Name);
                    CollectFromType(p.Type, sink);
                }

                sink.WithBoundNames(arrow.Parameters.Select(p => p.Name), () =>
                    CollectFromStatements(arrow.Body, sink)
                );

                break;
            case TsNamedFunctionExpression namedFunction:
                // Bound only inside its own body, but that body is exactly
                // where a generated helper call would be shadowed by it.
                sink.ShadowingNames.Add(namedFunction.Name);
                foreach (var p in namedFunction.Parameters)
                    sink.ShadowingNames.Add(p.Name);

                sink.WithBoundNames(
                    namedFunction.Parameters.Select(p => p.Name).Append(namedFunction.Name),
                    () => CollectFromStatements(namedFunction.Body, sink)
                );

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
                CollectFromType(cast.Type, sink);
                break;
            case TsTemplate template:
                // Templates carry real TS expression nodes for the receiver and each
                // argument; the printer expands them into the call site, so the import
                // collector must descend into them too. Without this case, identifiers
                // referenced only inside [Emit] / [MapMethod]/[MapProperty] templates
                // would either be missed entirely or be marked as type-only when they
                // need to be value imports (e.g., a `new SomeRecord(...)` substituted
                // into the template).
                CollectTemplateReceivers(template, sink);

                foreach (var arg in template.Arguments)
                    CollectFromExpression(arg, sink);

                // Full `TsType` placeholders (a constructor mapping's type
                // arguments) sit in type positions: walked as type references.
                foreach (var typeArgument in template.TypeArguments)
                    CollectFromType(typeArgument, sink);

                // `$T0`/`$T1` placeholders embed generic type-argument names
                // verbatim into the template body — those names must be
                // resolved as referenced identifiers so the import collector
                // can pull in the matching declaration. A type argument the
                // template does not embed is never printed, so it imports
                // nothing (a `[PlainObject]` one has no value to import, #651).
                // PascalCase guard skips primitive type aliases (`string`,
                // `number`, `boolean`) that don't need import lines.
                var embeddedTypeArguments = TsTemplatePlaceholder.ReferencedTypeArgumentIndices(
                    template.Template
                );

                foreach (var (index, typeArg) in template.TypeArgumentNames.Index())
                {
                    if (typeArg.Length == 0 || !char.IsUpper(typeArg[0]))
                        continue;

                    if (!embeddedTypeArguments.Contains(index))
                        continue;

                    names.Add(typeArg);
                    valueNames.Add(typeArg);
                }

                // Runtime helper identifiers carried alongside the template (e.g.,
                // "dayNumber", "listRemove", "immutableInsert") from
                // [MapMethod(..., RuntimeImports = "...")] declarations. The walker can't
                // see these inside the opaque template text, so the BclMapper threads
                // them through here as a separate field. They go into the dedicated
                // `templateRuntimeHelpers` set so the Collect entry point can emit a
                // single bundled `import { ... } from "metano-runtime"` line.
                foreach (var helper in template.RuntimeImports)
                    templateRuntimeHelpers.Add(helper);
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
    /// Collects the receiver of a template only where the printed template uses it:
    /// <c>$this</c> prints the receiver, and <c>$Type</c> prints the receiver's type by
    /// its own exported name, which the template declares instead of the walker guessing
    /// it from the text. A receiver the template never prints imports nothing: a guard's
    /// exception class, or a type named through an alias (#651, #675).
    /// </summary>
    private void CollectTemplateReceivers(TsTemplate template, ImportCollectionSink sink)
    {
        if (
            template.Receiver is { } receiver
            && TsTemplatePlaceholder.ReferencesReceiver(template.Template)
        )
            CollectFromExpression(receiver, sink);

        if (
            template.ReceiverType is { } receiverType
            && TsTemplatePlaceholder.ReferencesReceiverType(template.Template)
        )
            CollectFromExpression(receiverType, sink);
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
            // A component tag carrying a cross-package origin resolves through the
            // cross-package channel (`import { Counter } from "pkg/..."` + a recorded
            // dependency), exactly like a non-JSX cross-package type ref. Without
            // this it would be mis-registered as an intra-project name and emit a
            // dangling local import. The tag is a value (used as `<Counter />`).
            if (jsx.Origin is { } origin)
                sink.CrossPackageOrigins[jsx.TagName] = origin;
            else
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
    /// The importable binding of a possibly dotted type name: a nested type
    /// (<c>Outer.Inner</c>) is reached through its top-level container's import.
    /// </summary>
    private static void RecordModuleExport(
        Dictionary<string, SortedSet<string>> ownersByModuleExport,
        TsModuleExportReference exportReference
    )
    {
        if (!ownersByModuleExport.TryGetValue(exportReference.Name, out var owners))
        {
            owners = new SortedSet<string>(StringComparer.Ordinal);
            ownersByModuleExport[exportReference.Name] = owners;
        }

        owners.Add(exportReference.OwnerTypeName);
    }

    private static string RootSegment(string typeName)
    {
        var dot = typeName.IndexOf('.');

        return dot < 0 ? typeName : typeName[..dot];
    }

    /// <summary>
    /// Checks if a name is a runtime type check function from <c>metano-runtime</c>.
    /// </summary>
    private static IEnumerable<string> ValueImportedNames(IEnumerable<TsImport> imports) =>
        imports
            .Where(import => !import.TypeOnly)
            .SelectMany(import =>
                import.Names.Where(name => import.TypeOnlyNames?.Contains(name) != true)
            );
}

/// <summary>
/// Bundle of collection buckets threaded through the <see cref="ImportCollector"/>
/// reference walker. Every walker method writes into the same instance; the entry
/// point (<see cref="ImportCollector.Collect"/>) drains the buckets after the walk
/// to build the file's import lines.
/// </summary>
internal sealed class ImportCollectionSink(
    HashSet<string> names,
    HashSet<string> valueNames,
    HashSet<string> runtimeHelperReferences,
    HashSet<string> runtimeHelperTypeReferences,
    HashSet<string> templateRuntimeHelpers,
    HashSet<string> shadowingNames,
    Dictionary<string, TsTypeOrigin> crossPackageOrigins,
    List<IrExternalImport> templateExternals,
    HashSet<string> linqPipeHelpers,
    Dictionary<string, SortedSet<string>> ownersByModuleExport
)
{
    private readonly Stack<IReadOnlySet<string>> _boundScopes = new();

    public HashSet<string> Names { get; } = names;

    public HashSet<string> ValueNames { get; } = valueNames;

    public HashSet<string> RuntimeHelperReferences { get; } = runtimeHelperReferences;

    public HashSet<string> RuntimeHelperTypeReferences { get; } = runtimeHelperTypeReferences;

    public HashSet<string> TemplateRuntimeHelpers { get; } = templateRuntimeHelpers;

    public HashSet<string> ShadowingNames { get; } = shadowingNames;

    public Dictionary<string, TsTypeOrigin> CrossPackageOrigins { get; } = crossPackageOrigins;

    public List<IrExternalImport> TemplateExternals { get; } = templateExternals;

    public HashSet<string> LinqPipeHelpers { get; } = linqPipeHelpers;

    public Dictionary<string, SortedSet<string>> OwnersByModuleExport { get; } =
        ownersByModuleExport;

    /// <summary>
    /// Whether <paramref name="name"/> is bound by an enclosing function, block,
    /// <c>foreach</c> or <c>catch</c> of the node being walked: such a name is a
    /// local, whatever another module exports under it.
    /// </summary>
    public bool IsLocallyBound(string name) =>
        _boundScopes.Any(scope => scope.Contains(RootOf(name)));

    /// <summary>Runs <paramref name="walk"/> with <paramref name="names"/> bound.</summary>
    public void WithBoundNames(IEnumerable<string> names, Action walk)
    {
        _boundScopes.Push(names.ToHashSet(StringComparer.Ordinal));

        try
        {
            walk();
        }
        finally
        {
            _boundScopes.Pop();
        }
    }

    private static string RootOf(string name)
    {
        var dot = name.IndexOf('.');

        return dot < 0 ? name : name[..dot];
    }
}
