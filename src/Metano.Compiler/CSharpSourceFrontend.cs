using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Metano.Compiler;

/// <summary>
/// C# source frontend: opens a <c>.csproj</c> through
/// <see cref="MSBuildWorkspace"/>, runs Roslyn, and produces an
/// <see cref="IrCompilation"/> the downstream backends consume.
/// <para>
/// This is the first increment on the frontend / core split plan
/// (ADR-0013 follow-up). The loader is in place; the
/// <see cref="IrCompilation"/> fields beyond the basics are
/// deliberately left empty for now — the existing
/// <c>TypeTransformer</c> and <c>DartTransformer</c> still run their
/// own discovery + extraction off <see cref="LoadedCompilation"/>.
/// Subsequent PRs migrate that state onto <see cref="IrCompilation"/>
/// one field at a time and retire the escape hatch.
/// </para>
/// </summary>
public sealed class CSharpSourceFrontend : ISourceFrontend
{
    /// <inheritdoc />
    public string Name => "csharp";

    /// <inheritdoc />
    public Compilation? LoadedCompilation { get; private set; }

    /// <inheritdoc />
    public int LoadErrorCount { get; private set; }

    public async Task<IrCompilation> ExtractAsync(
        string projectPath,
        CancellationToken ct = default,
        TargetLanguage target = TargetLanguage.TypeScript
    )
    {
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        var diagnostics = new List<MetanoDiagnostic>();

        var (compilation, errorCount) = await LoadCompilationAsync(projectPath, diagnostics, ct);
        LoadedCompilation = compilation;
        LoadErrorCount = errorCount;

        return BuildIrCompilation(
            compilation,
            fallbackAssemblyName: assemblyName,
            diagnostics: diagnostics,
            target: target
        );
    }

    /// <summary>
    /// Builds an <see cref="IrCompilation"/> from an already-loaded Roslyn
    /// <see cref="Compilation"/>. Used by the test suite (and any future
    /// in-process caller) so the frontend's extraction can be exercised
    /// without going through <see cref="MSBuildWorkspace"/>. Mirrors the
    /// production path: stores the compilation on
    /// <see cref="LoadedCompilation"/>, resets <see cref="LoadErrorCount"/>
    /// to <c>0</c>, and returns the populated record.
    /// </summary>
    public IrCompilation ExtractFromCompilation(
        Compilation compilation,
        TargetLanguage target = TargetLanguage.TypeScript
    )
    {
        LoadedCompilation = compilation;
        LoadErrorCount = 0;
        return BuildIrCompilation(
            compilation,
            fallbackAssemblyName: compilation.AssemblyName ?? "",
            diagnostics: new List<MetanoDiagnostic>(),
            target: target
        );
    }

    private static IrCompilation BuildIrCompilation(
        Compilation? compilation,
        string fallbackAssemblyName,
        List<MetanoDiagnostic> diagnostics,
        TargetLanguage target
    )
    {
        // Fields beyond what's already populated stay empty during the
        // incremental migration. Downstream targets fall back to
        // `LoadedCompilation` for the rest until follow-up PRs wire them
        // onto `IrCompilation`.
        IReadOnlyDictionary<string, IrExternalImport> externalImports = compilation is null
            ? new Dictionary<string, IrExternalImport>(StringComparer.Ordinal)
            : BuildExternalImports(compilation, target, diagnostics);

        IReadOnlyDictionary<string, IrTypeOrigin> crossAssemblyOrigins;
        IReadOnlySet<string> assembliesNeedingEmitPackage;
        if (compilation is null)
        {
            crossAssemblyOrigins = new Dictionary<string, IrTypeOrigin>(StringComparer.Ordinal);
            assembliesNeedingEmitPackage = new HashSet<string>(StringComparer.Ordinal);
        }
        else
        {
            (crossAssemblyOrigins, assembliesNeedingEmitPackage) = BuildCrossAssemblyState(
                compilation,
                target
            );
        }

        var assemblyWideTranspile = compilation is not null && compilation.HasTranspileAssembly();

        var declarativeMappings = compilation is null
            ? default
            : BuildDeclarativeMappings(compilation, diagnostics);

        var typeNamesBySymbol = compilation is null
            ? null
            : BuildTypeNamesBySymbol(compilation, target, assemblyWideTranspile);

        var guardableTypeKeys = compilation is null
            ? null
            : BuildGuardableTypeKeys(compilation, assemblyWideTranspile);

        var transpilableTypes = compilation is null
            ? null
            : BuildTranspilableTypes(compilation, target, assemblyWideTranspile);

        return new IrCompilation(
            AssemblyName: compilation?.AssemblyName ?? fallbackAssemblyName,
            PackageName: compilation is null
                ? null
                : SymbolHelper.GetEmitPackage(
                    compilation.Assembly,
                    targetEnumValue: (int)target.ToEmitTarget()
                ),
            AssemblyWideTranspile: assemblyWideTranspile,
            Modules: Array.Empty<IrModule>(),
            ReferencedModules: Array.Empty<IrModule>(),
            CrossAssemblyOrigins: crossAssemblyOrigins,
            ExternalImports: externalImports,
            BclExports: compilation is null
                ? new Dictionary<string, IrBclExport>(StringComparer.Ordinal)
                : BuildBclExports(compilation),
            AssembliesNeedingEmitPackage: assembliesNeedingEmitPackage,
            Diagnostics: diagnostics,
            LocalRootNamespace: compilation is null
                ? ""
                : ComputeLocalRootNamespace(compilation, assemblyWideTranspile),
            DeclarativeMethodMappings: declarativeMappings.Methods,
            DeclarativePropertyMappings: declarativeMappings.Properties,
            ChainMethodsByWrapper: declarativeMappings.ChainMethodsByWrapper,
            TypeNamesBySymbol: typeNamesBySymbol,
            GuardableTypeKeys: guardableTypeKeys,
            TranspilableTypes: transpilableTypes
        );
    }

    /// <summary>
    /// Computes the longest common namespace prefix of the transpilable types
    /// declared in <paramref name="compilation"/>'s own assembly. Mirrors the
    /// target-side filter (<see cref="SymbolHelper.IsTranspilable"/>) so the
    /// prefix stays identical to what <c>TypeTransformer</c> used to compute
    /// inline — including internal <c>[Transpile]</c> types that would be
    /// missed by a public-only walker, and the synthetic Program type that
    /// <c>TypeTransformer.DiscoverTranspilableTypes</c> injects for C# 9+
    /// top-level statements under <c>[assembly: TranspileAssembly]</c>.
    /// </summary>
    private static string ComputeLocalRootNamespace(
        Compilation compilation,
        bool assemblyWideTranspile
    )
    {
        var currentAssembly = compilation.Assembly;
        var transpilable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    transpilable.Add(type);
            }
        );

        if (assemblyWideTranspile && TryGetTopLevelProgramType(compilation, out var programType))
            transpilable.Add(programType);

        return ComputeRootNamespaceFromTypes(transpilable);
    }

    /// <summary>
    /// Detects C# 9+ top-level statements and returns the compiler-synthesized
    /// containing type (usually <c>Program</c>) that <c>TypeTransformer</c>
    /// treats as transpilable under assembly-wide mode. Returns <c>false</c>
    /// when there are no global statements, when Roslyn reports no entry
    /// point, or when the program type opts out via
    /// <c>[ExportedAsModule]</c>.
    /// </summary>
    private static bool TryGetTopLevelProgramType(
        Compilation compilation,
        out INamedTypeSymbol programType
    )
    {
        if (compilation is not CSharpCompilation csharpComp)
        {
            programType = null!;
            return false;
        }

        var hasGlobalStatements = compilation.SyntaxTrees.Any(t =>
            t.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any()
        );
        if (!hasGlobalStatements)
        {
            programType = null!;
            return false;
        }

        var entryPoint = csharpComp.GetEntryPoint(CancellationToken.None);
        if (entryPoint?.ContainingType is not { } containingType)
        {
            programType = null!;
            return false;
        }

        if (SymbolHelper.HasExportedAsModule(containingType))
        {
            programType = null!;
            return false;
        }

        programType = containingType;
        return true;
    }

    /// <summary>
    /// Pre-resolves the target-facing emitted name for every type the
    /// backend may look up by Roslyn symbol. Covers transpilable types
    /// (top-level + nested) from the current assembly, transpilable types
    /// from referenced assemblies that declared <c>[EmitPackage]</c> for
    /// the active target, and <c>[Import]</c> placeholders from any public
    /// top-level symbol in scope. Types carrying <c>[NoTranspile]</c> or
    /// <c>[NoEmit(target)]</c> are excluded on both sides — the backend
    /// never asks for their name since they do not participate in emission.
    /// The resulting dictionary is keyed by
    /// <see cref="SymbolHelper.GetCrossAssemblyOriginKey"/> (the
    /// assembly-qualified stable full name) so two referenced assemblies
    /// that expose types with identical stable full names cannot collapse
    /// each other's target-name override. Backends resolve target names
    /// through a single dict lookup instead of duplicating
    /// <see cref="SymbolHelper.GetNameOverride"/> in every bridge.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildTypeNamesBySymbol(
        Compilation compilation,
        TargetLanguage target,
        bool assemblyWideTranspile
    )
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentAssembly = compilation.Assembly;

        void RegisterSymbol(INamedTypeSymbol type) =>
            map.TryAdd(
                type.GetCrossAssemblyOriginKey(),
                SymbolHelper.GetNameOverride(type, target) ?? type.Name
            );

        // Visits every top-level type and every nested type so
        // `[Name(target, …)]` overrides on nested declarations end up in
        // the map too. The caller's `register` decides which symbols
        // actually land in the dict.
        void Visit(INamedTypeSymbol type, Action<INamedTypeSymbol> register)
        {
            register(type);
            foreach (var nested in type.GetTypeMembers())
                Visit(nested, register);
        }

        // Current assembly — every transpilable type (matches the target-side
        // DiscoverTranspilableTypes + nested-emission filter) plus every
        // `[Import]` placeholder the public-only walker would see.
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
                Visit(
                    type,
                    sym =>
                    {
                        if (
                            SymbolHelper.IsTranspilable(sym, assemblyWideTranspile, currentAssembly)
                        )
                            RegisterSymbol(sym);
                        else if (
                            sym.DeclaredAccessibility == Accessibility.Public
                            && SymbolHelper.GetImport(sym) is not null
                        )
                            RegisterSymbol(sym);
                    }
                )
        );

        // Referenced assemblies that opted into transpilation with an
        // [EmitPackage] for the active target. Mirrors the
        // BuildCrossAssemblyState filter so the same set of emittable types
        // ends up in the dict — [NoTranspile] / [NoEmit(target)] types are
        // excluded, [Import] placeholders on the referenced side are still
        // registered so consumers can resolve their alias by symbol.
        foreach (var (asm, _) in EnumerateTranspilableReferencedAssemblies(compilation, target))
        {
            CollectTopLevelTypes(
                asm.GlobalNamespace,
                type =>
                    Visit(
                        type,
                        sym =>
                        {
                            if (sym.DeclaredAccessibility != Accessibility.Public)
                                return;
                            if (SymbolHelper.HasNoTranspile(sym))
                                return;
                            if (SymbolHelper.HasNoEmit(sym, target))
                                return;
                            RegisterSymbol(sym);
                        }
                    )
            );
        }

        return map;
    }

    /// <summary>
    /// Collects the assembly-qualified stable full name of every
    /// transpilable top-level type in the current assembly that declares
    /// <c>[GenerateGuard]</c>. Backends pair this set with their own
    /// guard-naming convention (TypeScript: <c>is{Name}</c>) to decide
    /// when a referenced identifier is actually a guard import, without
    /// re-reading the attribute at every call site. Current-assembly /
    /// top-level only — mirrors the top-level transpilable set the
    /// legacy <c>_guardNameToTypeMap</c> init loop walked, minus the
    /// synthetic Program type (C# 9+ top-level statements) which can
    /// never carry <c>[GenerateGuard]</c>.
    /// </summary>
    private static IReadOnlySet<string> BuildGuardableTypeKeys(
        Compilation compilation,
        bool assemblyWideTranspile
    )
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var currentAssembly = compilation.Assembly;

        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    return;
                if (!SymbolHelper.HasGenerateGuard(type))
                    return;
                keys.Add(type.GetCrossAssemblyOriginKey());
            }
        );

        return keys;
    }

    /// <summary>
    /// Projects every current-assembly top-level transpilable type to an
    /// <see cref="IrTranspilableTypeRef"/> and indexes it under both its
    /// C# source name and its target-facing TS name (when
    /// <c>[Name(target, …)]</c> diverges). Lets the backend resolve a
    /// bare identifier walked out of the generated AST into emit
    /// metadata — origin key, namespace, on-disk file name, string-enum
    /// flag — without going back to the Roslyn symbol table. Current
    /// assembly, top-level only, nested types excluded. The synthetic
    /// C# 9+ top-level <c>Program</c> type is included under
    /// <c>[assembly: TranspileAssembly]</c> so the target's import
    /// collector resolves module-entry references consistently with
    /// every other transpilable type.
    /// </summary>
    private static IReadOnlyDictionary<string, IrTranspilableTypeRef> BuildTranspilableTypes(
        Compilation compilation,
        TargetLanguage target,
        bool assemblyWideTranspile
    )
    {
        var map = new Dictionary<string, IrTranspilableTypeRef>(StringComparer.Ordinal);
        var currentAssembly = compilation.Assembly;

        void Register(INamedTypeSymbol type)
        {
            var tsName = SymbolHelper.GetNameOverride(type, target) ?? type.Name;
            var ns = type.ContainingNamespace.IsGlobalNamespace
                ? ""
                : type.ContainingNamespace.ToDisplayString();
            var emitInFile = SymbolHelper.GetEmitInFile(type);
            var fileBase = emitInFile is { Length: > 0 } ? emitInFile : tsName;
            var fileName = SymbolHelper.ToKebabCase(fileBase);
            var entry = new IrTranspilableTypeRef(
                Key: type.GetCrossAssemblyOriginKey(),
                TsName: tsName,
                Namespace: ns,
                FileName: fileName,
                IsStringEnum: SymbolHelper.HasStringEnum(type)
            );
            map.TryAdd(type.Name, entry);
            if (tsName != type.Name)
                map.TryAdd(tsName, entry);
        }

        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    return;
                Register(type);
            }
        );

        if (assemblyWideTranspile && TryGetTopLevelProgramType(compilation, out var programType))
            Register(programType);

        return map;
    }

    /// <summary>
    /// Reads <c>[assembly: MapMethod]</c> / <c>[assembly: MapProperty]</c>
    /// declarations from the current assembly plus every referenced
    /// assembly and indexes them by
    /// (<see cref="SymbolHelper.GetStableFullName"/>, member-name). Also
    /// computes the per-wrapper set of mapped JS method names so the target
    /// can recognize already-wrapped receivers. Mirrors the legacy
    /// <c>DeclarativeMappingRegistry.BuildFromCompilation</c> attribute
    /// walk, dropping the symbol-keyed dictionaries whose Roslyn consumers
    /// retired with ADR-0013.
    /// </summary>
    private static (
        IReadOnlyDictionary<
            (string DeclaringTypeFullName, string MemberName),
            IReadOnlyList<DeclarativeMappingEntry>
        > Methods,
        IReadOnlyDictionary<
            (string DeclaringTypeFullName, string MemberName),
            DeclarativeMappingEntry
        > Properties,
        IReadOnlyDictionary<string, IReadOnlySet<string>> ChainMethodsByWrapper
    ) BuildDeclarativeMappings(Compilation compilation, List<MetanoDiagnostic> diagnostics)
    {
        var methods = new Dictionary<(string, string), List<DeclarativeMappingEntry>>();
        var properties = new Dictionary<(string, string), DeclarativeMappingEntry>();

        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                assemblies.Add(assembly);
        }

        foreach (var assembly in assemblies)
        {
            foreach (var attr in assembly.GetAttributes())
            {
                switch (attr.AttributeClass?.Name)
                {
                    case "MapMethodAttribute":
                        TryRegisterMapping(attr, "JsMethod", methods, diagnostics);
                        break;
                    case "MapPropertyAttribute":
                        TryRegisterProperty(attr, "JsProperty", properties, diagnostics);
                        break;
                }
            }
        }

        var chainMethodsByWrapper = new Dictionary<string, HashSet<string>>();
        foreach (var entries in methods.Values)
        {
            foreach (var entry in entries)
            {
                if (entry.WrapReceiver is null || entry.JsName is null)
                    continue;
                if (!chainMethodsByWrapper.TryGetValue(entry.WrapReceiver, out var set))
                {
                    set = [];
                    chainMethodsByWrapper[entry.WrapReceiver] = set;
                }
                set.Add(entry.JsName);
            }
        }

        var readonlyMethods = methods.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DeclarativeMappingEntry>)kv.Value
        );
        var readonlyChains = chainMethodsByWrapper.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value
        );

        return (readonlyMethods, properties, readonlyChains);
    }

    private static void TryRegisterMapping(
        AttributeData attr,
        string renameNamedArg,
        Dictionary<(string, string), List<DeclarativeMappingEntry>> target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var key = ReadMappingKey(attr);
        if (key is null)
            return;

        var entry = ReadMappingEntry(attr, renameNamedArg, diagnostics);
        if (entry is null)
            return;

        if (!target.TryGetValue(key.Value, out var list))
        {
            list = [];
            target[key.Value] = list;
        }
        list.Add(entry);
    }

    private static void TryRegisterProperty(
        AttributeData attr,
        string renameNamedArg,
        Dictionary<(string, string), DeclarativeMappingEntry> target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var key = ReadMappingKey(attr);
        if (key is null)
            return;

        var entry = ReadMappingEntry(attr, renameNamedArg, diagnostics);
        if (entry is null)
            return;

        target[key.Value] = entry;
    }

    private static (string DeclaringTypeFullName, string MemberName)? ReadMappingKey(
        AttributeData attr
    )
    {
        if (attr.ConstructorArguments.Length < 2)
            return null;
        if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol declaringType)
            return null;
        if (attr.ConstructorArguments[1].Value is not string memberName)
            return null;
        return (declaringType.GetStableFullName(), memberName);
    }

    private static DeclarativeMappingEntry? ReadMappingEntry(
        AttributeData attr,
        string renameNamedArg,
        List<MetanoDiagnostic> diagnostics
    )
    {
        string? jsName = null;
        string? jsTemplate = null;
        string? whenArg0StringEquals = null;
        string? wrapReceiver = null;
        string? runtimeImports = null;
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case var k when k == renameNamedArg:
                    jsName = named.Value.Value as string;
                    break;
                case "JsTemplate":
                    jsTemplate = named.Value.Value as string;
                    break;
                case "WhenArg0StringEquals":
                    whenArg0StringEquals = named.Value.Value as string;
                    break;
                case "WrapReceiver":
                    wrapReceiver = named.Value.Value as string;
                    break;
                case "RuntimeImports":
                    runtimeImports = named.Value.Value as string;
                    break;
            }
        }

        if (jsName is null && jsTemplate is null)
            return null;

        // JsName and JsTemplate are mutually exclusive — template takes
        // precedence (it's the more specific lowering form). Surface MS0004
        // so a user who meant only one of the two can spot the conflict.
        if (jsName is not null && jsTemplate is not null)
        {
            var target =
                attr.ConstructorArguments.Length >= 2
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol owner
                && attr.ConstructorArguments[1].Value is string memberName
                    ? $"{owner.ToDisplayString()}.{memberName}"
                    : attr.AttributeClass?.ToDisplayString() ?? "<unknown>";
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.ConflictingAttributes,
                    $"Declarative mapping on '{target}' sets both "
                        + $"{renameNamedArg} ('{jsName}') and JsTemplate ('{jsTemplate}') — "
                        + $"these are mutually exclusive. Keeping JsTemplate and ignoring "
                        + $"{renameNamedArg}.",
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                )
            );
            jsName = null;
        }

        return new DeclarativeMappingEntry(
            jsName,
            jsTemplate,
            whenArg0StringEquals,
            wrapReceiver,
            runtimeImports
        );
    }

    /// <summary>
    /// Longest common namespace prefix across <paramref name="types"/>,
    /// skipping types declared in the global namespace so a single
    /// unnamespaced entry cannot collapse the shared root on its own.
    /// </summary>
    private static string ComputeRootNamespaceFromTypes(IEnumerable<INamedTypeSymbol> types)
    {
        var namespaces = new List<string>();
        foreach (var type in types)
        {
            if (type.ContainingNamespace.IsGlobalNamespace)
                continue;
            namespaces.Add(type.ContainingNamespace.ToDisplayString());
        }
        return NamespaceUtilities.FindCommonPrefix(namespaces);
    }

    /// <summary>
    /// Walks <see cref="Compilation.References"/> and partitions every
    /// referenced assembly that opted into transpilation
    /// (<c>[TranspileAssembly]</c>) into one of two buckets:
    /// <list type="bullet">
    ///   <item>If it also declares <c>[EmitPackage]</c> for the active
    ///   target, every public top-level type that is not <c>[Import]</c>,
    ///   <c>[NoEmit]</c>, or <c>[NoTranspile]</c> contributes an
    ///   <see cref="IrTypeOrigin"/> keyed by
    ///   <see cref="SymbolHelper.GetCrossAssemblyOriginKey(ITypeSymbol)"/>.</item>
    ///   <item>Otherwise the assembly's metadata name is collected so the
    ///   target can later raise <see cref="DiagnosticCodes.CrossPackageResolution"/>
    ///   (MS0007) at the consumer site.</item>
    /// </list>
    /// <paramref name="target"/> drives the per-target <c>[NoEmit]</c>
    /// filter so a Dart-specific <c>[NoEmit]</c> cannot poison the
    /// TypeScript origin table (and vice versa).
    /// </summary>
    private static (
        IReadOnlyDictionary<string, IrTypeOrigin> CrossAssemblyOrigins,
        IReadOnlySet<string> AssembliesNeedingEmitPackage
    ) BuildCrossAssemblyState(Compilation compilation, TargetLanguage target)
    {
        var origins = new Dictionary<string, IrTypeOrigin>(StringComparer.Ordinal);
        var needingPackage = new HashSet<string>(StringComparer.Ordinal);

        foreach (
            var (asm, packageInfo) in EnumerateTranspilableReferencedAssemblies(
                compilation,
                target,
                onTranspilableWithoutEmitPackage: name => needingPackage.Add(name)
            )
        )
        {
            var assemblyTypes = new List<INamedTypeSymbol>();
            CollectTopLevelTypes(
                asm.GlobalNamespace,
                type =>
                {
                    if (type.DeclaredAccessibility == Accessibility.Public)
                        assemblyTypes.Add(type);
                }
            );

            // Filter the same way the legacy CollectTypesFromNamespace does — anything
            // that wouldn't be discovered for emission must not influence the assembly
            // root namespace either, otherwise an [NoEmit] type sitting in an unrelated
            // namespace would silently shrink the prefix used for import subpaths.
            var emittedAssemblyTypes = assemblyTypes
                .Where(type =>
                    SymbolHelper.GetImport(type) is null
                    && !SymbolHelper.HasNoTranspile(type)
                    && !SymbolHelper.HasNoEmit(type, target)
                )
                .ToList();

            var rootNamespace = ComputeRootNamespaceFromTypes(emittedAssemblyTypes);

            foreach (var type in emittedAssemblyTypes)
            {
                origins[type.GetCrossAssemblyOriginKey()] = new IrTypeOrigin(
                    PackageId: packageInfo.Name,
                    Namespace: type.ContainingNamespace.IsGlobalNamespace
                        ? null
                        : type.ContainingNamespace.ToDisplayString(),
                    AssemblyRootNamespace: rootNamespace,
                    VersionHint: packageInfo.Version
                );
            }
        }

        return (origins, needingPackage);
    }

    /// <summary>
    /// Walks every public top-level type in the current compilation plus
    /// every referenced assembly that opted into transpilation
    /// (<c>[TranspileAssembly]</c>) and declared an <c>[EmitPackage]</c>
    /// for the active target, surfacing those carrying
    /// <c>[Import("name", from)]</c> as <see cref="IrExternalImport"/>
    /// entries keyed by the C# type's simple source name <em>and</em> by
    /// the per-target <c>[Name(target, …)]</c> alias when it differs —
    /// both keys point at the same <see cref="IrExternalImport"/> value so
    /// the backend can resolve imports by whichever name it is holding
    /// (source, emitted, or either after rename). Retires the duplicate
    /// walker that used to live in <c>TypeTransformer.RegisterTsNameAliases</c>.
    /// The local assembly is walked first so on collision its entry wins,
    /// matching the legacy ordering in
    /// <c>TypeTransformer.DiscoverCrossAssemblyTypes</c>.
    /// <para>
    /// Conflict policy: first mapping wins and any divergent
    /// re-registration produces a
    /// <see cref="DiagnosticCodes.AmbiguousConstruct"/> warning surfaced
    /// through <see cref="IrCompilation.Diagnostics"/>.
    /// </para>
    /// </summary>
    private static Dictionary<string, IrExternalImport> BuildExternalImports(
        Compilation compilation,
        TargetLanguage target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var map = new Dictionary<string, IrExternalImport>(StringComparer.Ordinal);

        AddImportsFromAssembly(compilation.Assembly, target, map, diagnostics);

        foreach (var (asm, _) in EnumerateTranspilableReferencedAssemblies(compilation, target))
            AddImportsFromAssembly(asm, target, map, diagnostics);

        return map;
    }

    /// <summary>
    /// Yields every referenced assembly (excluding the one being compiled)
    /// that opted into transpilation via <c>[TranspileAssembly]</c> and
    /// declared an <c>[EmitPackage]</c> for the active target. When
    /// <paramref name="onTranspilableWithoutEmitPackage"/> is supplied it
    /// receives the metadata name of every assembly that opted in but
    /// omitted <c>[EmitPackage]</c> — callers that need to surface that
    /// case (MS0007 bookkeeping in
    /// <see cref="BuildCrossAssemblyState"/>) pass a collector; callers
    /// that just want the fully-configured set (import aggregation) leave
    /// it null.
    /// </summary>
    private static IEnumerable<(
        IAssemblySymbol Assembly,
        SymbolHelper.EmitPackageInfo PackageInfo
    )> EnumerateTranspilableReferencedAssemblies(
        Compilation compilation,
        TargetLanguage target,
        Action<string>? onTranspilableWithoutEmitPackage = null
    )
    {
        var emitTargetValue = (int)target.ToEmitTarget();
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
            {
                onTranspilableWithoutEmitPackage?.Invoke(asm.Name);
                continue;
            }

            yield return (asm, packageInfo);
        }
    }

    /// <summary>
    /// Walks <paramref name="assembly"/>'s public top-level types and
    /// registers any <c>[Import]</c>-annotated entry into
    /// <paramref name="map"/> under the C# source name plus, when a
    /// <c>[Name(target, …)]</c> alias for the active
    /// <paramref name="target"/> differs from the source name, that alias
    /// too. Both keys point at the same <see cref="IrExternalImport"/>
    /// value so the backend can look the import up by whichever name it
    /// is holding. A <c>[Name(…)]</c> targeted at any language other than
    /// <paramref name="target"/> is deliberately ignored on this pass.
    /// Mirrors the legacy <c>TypeTransformer.RegisterExternalImportMapping</c>
    /// conflict policy (first mapping wins; collisions produce MS0003).
    /// </summary>
    private static void AddImportsFromAssembly(
        IAssemblySymbol assembly,
        TargetLanguage target,
        Dictionary<string, IrExternalImport> map,
        List<MetanoDiagnostic> diagnostics
    )
    {
        CollectTopLevelTypes(
            assembly.GlobalNamespace,
            type =>
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                    return;

                var import = SymbolHelper.GetImport(type);
                if (import is null)
                    return;

                var entry = new IrExternalImport(
                    Name: import.Name,
                    From: import.From,
                    IsDefault: import.AsDefault,
                    Version: import.Version
                );

                // Register under the C# source name first so it remains
                // the canonical lookup key for callers that only know the
                // C# identifier.
                TryRegisterImport(type.Name, entry, type, map, diagnostics);

                // Additionally register the per-target [Name(target, …)]
                // alias so the backend can resolve an import by the
                // emitted identifier without re-reading Roslyn. Skip when
                // the alias collapses to the source name (no-op) or is
                // absent.
                var targetName = SymbolHelper.GetNameOverride(type, target);
                if (targetName is not null && targetName != type.Name)
                    TryRegisterImport(targetName, entry, type, map, diagnostics);
            }
        );
    }

    /// <summary>
    /// Inserts <paramref name="entry"/> into <paramref name="map"/> under
    /// <paramref name="key"/>, enforcing the first-wins conflict policy:
    /// a value-equal re-registration is a silent no-op (the same
    /// <see cref="IrExternalImport"/> can legitimately land twice — once
    /// under the source name and once under its per-target alias), while
    /// any divergent re-registration is reported as MS0003 and dropped.
    /// </summary>
    private static void TryRegisterImport(
        string key,
        IrExternalImport entry,
        INamedTypeSymbol owner,
        Dictionary<string, IrExternalImport> map,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (!map.TryGetValue(key, out var existing))
        {
            map[key] = entry;
            return;
        }

        if (existing == entry)
            return;

        diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousConstruct,
                $"External import name collision for '{key}'. Keeping "
                    + $"'{existing.From}' ('{existing.Name}') and ignoring "
                    + $"conflicting mapping from '{owner.ToDisplayString()}' to "
                    + $"'{entry.From}' ('{entry.Name}').",
                owner.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// Recursively walks every top-level type (i.e. non-nested) declared
    /// under <paramref name="ns"/>, regardless of accessibility, and
    /// passes each to <paramref name="visit"/>. Callers that only want
    /// public types (BCL-wide transpile, cross-assembly emission) filter
    /// via the visitor; callers that need to see internal
    /// <c>[Transpile]</c> types (local root-namespace computation) leave
    /// the visitor unfiltered.
    /// </summary>
    private static void CollectTopLevelTypes(INamespaceSymbol ns, Action<INamedTypeSymbol> visit)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectTopLevelTypes(childNs, visit);
                    break;
                case INamedTypeSymbol type when type.ContainingType is null:
                    visit(type);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads <c>[ExportFromBcl]</c> declarations from every assembly in scope
    /// (referenced first, current assembly last so user-side overrides win on
    /// conflict) and returns them keyed by the BCL type's display string.
    /// Mirrors the legacy <c>TypeTransformer.LoadBclExportMappings</c>; the
    /// target-side copy stays in place until the contract flip migrates
    /// consumers onto the IR.
    /// </summary>
    private static Dictionary<string, IrBclExport> BuildBclExports(Compilation compilation)
    {
        var map = new Dictionary<string, IrBclExport>(StringComparer.Ordinal);

        foreach (var reference in compilation.References)
        {
            if (
                compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm
                && !SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly)
            )
                CollectBclExportsFromAssembly(asm, map);
        }
        CollectBclExportsFromAssembly(compilation.Assembly, map);

        return map;
    }

    private static void CollectBclExportsFromAssembly(
        IAssemblySymbol assembly,
        Dictionary<string, IrBclExport> sink
    )
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("ExportFromBclAttribute" or "ExportFromBcl"))
                continue;

            if (attr.ConstructorArguments.Length == 0)
                continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol typeArg)
                continue;

            var exportedName = "";
            var fromPackage = "";
            string? version = null;

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
                        var raw = namedArg.Value.Value?.ToString();
                        version = string.IsNullOrEmpty(raw) ? null : raw;
                        break;
                }
            }

            if (exportedName.Length == 0)
                continue;

            sink[typeArg.ToDisplayString()] = new IrBclExport(exportedName, fromPackage, version);
        }
    }

    /// <summary>
    /// Opens the project via <see cref="MSBuildWorkspace"/> and returns
    /// the resulting <see cref="Compilation"/>. Mirrors the previous
    /// <c>TranspilerHost.LoadCompilationAsync</c>: progress and errors
    /// also go to stdout/stderr so the CLI trace stays unchanged, but
    /// every failure is additionally appended to <paramref name="diagnostics"/>
    /// as a <see cref="DiagnosticCodes.FrontendLoadFailure"/> entry so
    /// the host (and any future programmatic caller) can react via
    /// <see cref="IrCompilation.Diagnostics"/>.
    /// </summary>
    private static async Task<(Compilation? Compilation, int ErrorCount)> LoadCompilationAsync(
        string projectPath,
        List<MetanoDiagnostic> diagnostics,
        CancellationToken ct
    )
    {
        if (!File.Exists(projectPath))
        {
            var message = $"Project not found: {projectPath}";
            Console.Error.WriteLine(message);
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.FrontendLoadFailure,
                    message
                )
            );
            return (null, 1);
        }

        Console.WriteLine($"Metano: Loading project {Path.GetFileName(projectPath)}...");

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                var message = $"Workspace error: {e.Diagnostic.Message}";
                Console.Error.WriteLine($"  {message}");
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.FrontendLoadFailure,
                        message
                    )
                );
            }
        });

        Console.WriteLine("  Opening MSBuild project...");
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);

        Console.WriteLine("  Project loaded.");
        Console.WriteLine("  Creating Roslyn compilation...");
        var compilation = await project.GetCompilationAsync(ct);

        Console.WriteLine("  Compilation created.");

        if (compilation is null)
        {
            const string message = "Failed to compile project.";
            Console.Error.WriteLine(message);
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.FrontendLoadFailure,
                    message
                )
            );
            return (null, 1);
        }

        var roslynErrors = compilation
            .GetDiagnostics(ct)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (roslynErrors.Count > 0)
        {
            Console.Error.WriteLine($"Compilation has {roslynErrors.Count} error(s):");
            foreach (var error in roslynErrors.Take(10))
            {
                Console.Error.WriteLine($"  {error}");
            }

            foreach (var error in roslynErrors)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.FrontendLoadFailure,
                        error.GetMessage(),
                        error.Location
                    )
                );
            }

            return (null, roslynErrors.Count);
        }

        return (compilation, 0);
    }
}
