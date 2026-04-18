using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
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
        CancellationToken ct = default
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
            diagnostics: diagnostics
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
    public IrCompilation ExtractFromCompilation(Compilation compilation)
    {
        LoadedCompilation = compilation;
        LoadErrorCount = 0;
        return BuildIrCompilation(
            compilation,
            fallbackAssemblyName: compilation.AssemblyName ?? "",
            diagnostics: new List<MetanoDiagnostic>()
        );
    }

    private static IrCompilation BuildIrCompilation(
        Compilation? compilation,
        string fallbackAssemblyName,
        List<MetanoDiagnostic> diagnostics
    )
    {
        // Fields beyond what's already populated stay empty during the
        // incremental migration. Downstream targets fall back to
        // `LoadedCompilation` for the rest until follow-up PRs wire them
        // onto `IrCompilation`.
        IReadOnlyDictionary<string, IrExternalImport> externalImports = compilation is null
            ? new Dictionary<string, IrExternalImport>(StringComparer.Ordinal)
            : BuildExternalImports(compilation, diagnostics);

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
                compilation
            );
        }

        return new IrCompilation(
            AssemblyName: compilation?.AssemblyName ?? fallbackAssemblyName,
            PackageName: null,
            AssemblyWideTranspile: false,
            Modules: Array.Empty<IrModule>(),
            ReferencedModules: Array.Empty<IrModule>(),
            CrossAssemblyOrigins: crossAssemblyOrigins,
            ExternalImports: externalImports,
            BclExports: compilation is null
                ? new Dictionary<string, IrBclExport>(StringComparer.Ordinal)
                : BuildBclExports(compilation),
            AssembliesNeedingEmitPackage: assembliesNeedingEmitPackage,
            Diagnostics: diagnostics
        );
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
    ///   <see cref="SymbolHelper.GetStableFullName(ITypeSymbol)"/>.</item>
    ///   <item>Otherwise the assembly's metadata name is collected so the
    ///   target can later raise <see cref="DiagnosticCodes.CrossPackageResolution"/>
    ///   (MS0007) at the consumer site.</item>
    /// </list>
    /// The active target is currently hardcoded to TypeScript / JavaScript
    /// (<c>EmitTarget</c> integer value <c>0</c>) — every consumer of this
    /// information today is JS-bound; introducing target awareness on the
    /// frontend is a separate concern bundled with the contract flip.
    /// </summary>
    private static (
        IReadOnlyDictionary<string, IrTypeOrigin> CrossAssemblyOrigins,
        IReadOnlySet<string> AssembliesNeedingEmitPackage
    ) BuildCrossAssemblyState(Compilation compilation)
    {
        var origins = new Dictionary<string, IrTypeOrigin>(StringComparer.Ordinal);
        var needingPackage = new HashSet<string>(StringComparer.Ordinal);

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

            var packageInfo = SymbolHelper.GetEmitPackageInfo(
                asm,
                targetEnumValue: (int)EmitTarget.JavaScript
            );
            if (packageInfo is null)
            {
                needingPackage.Add(asm.Name);
                continue;
            }

            var assemblyTypes = new List<INamedTypeSymbol>();
            CollectPublicTopLevelTypes(asm.GlobalNamespace, assemblyTypes);

            // Filter the same way the legacy CollectTypesFromNamespace does — anything
            // that wouldn't be discovered for emission must not influence the assembly
            // root namespace either, otherwise an [NoEmit] type sitting in an unrelated
            // namespace would silently shrink the prefix used for import subpaths.
            var emittedAssemblyTypes = assemblyTypes
                .Where(type =>
                    SymbolHelper.GetImport(type) is null
                    && !SymbolHelper.HasNoTranspile(type)
                    && !SymbolHelper.HasNoEmit(type, TargetLanguage.TypeScript)
                )
                .ToList();

            var rootNamespace = ComputeAssemblyRootNamespace(emittedAssemblyTypes);

            foreach (var type in emittedAssemblyTypes)
            {
                origins[type.GetStableFullName()] = new IrTypeOrigin(
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

    private static string ComputeAssemblyRootNamespace(IReadOnlyList<INamedTypeSymbol> types)
    {
        var namespaces = new List<string>(types.Count);
        foreach (var type in types)
        {
            if (type.ContainingNamespace.IsGlobalNamespace)
                continue;
            namespaces.Add(type.ContainingNamespace.ToDisplayString());
        }

        return NamespaceUtilities.FindCommonPrefix(namespaces);
    }

    /// <summary>
    /// Walks every public top-level type in the current compilation and
    /// surfaces those carrying <c>[Import("name", from)]</c> as
    /// <see cref="IrExternalImport"/> entries keyed by the C# type's simple
    /// source name. This intentionally covers only the source-name keys for
    /// the local assembly that the legacy
    /// <c>TypeTransformer._externalImportMap</c> exposed; cross-assembly
    /// <c>[Import]</c> aggregation and per-target <c>[Name]</c> aliasing
    /// remain on the consumer side until later migration steps wire them
    /// through the IR. Mirrors the legacy
    /// <c>TypeTransformer.RegisterExternalImportMapping</c> conflict policy:
    /// the first mapping wins and any divergent re-registration produces a
    /// <see cref="DiagnosticCodes.AmbiguousConstruct"/> warning surfaced
    /// through <see cref="IrCompilation.Diagnostics"/>.
    /// </summary>
    private static Dictionary<string, IrExternalImport> BuildExternalImports(
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var map = new Dictionary<string, IrExternalImport>(StringComparer.Ordinal);
        var types = new List<INamedTypeSymbol>();
        CollectPublicTopLevelTypes(compilation.Assembly.GlobalNamespace, types);

        foreach (var type in types)
        {
            var import = SymbolHelper.GetImport(type);
            if (import is null)
                continue;

            var entry = new IrExternalImport(
                Name: import.Name,
                From: import.From,
                IsDefault: import.AsDefault,
                Version: import.Version
            );

            if (map.TryGetValue(type.Name, out var existing))
            {
                if (existing == entry)
                    continue;

                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Warning,
                        DiagnosticCodes.AmbiguousConstruct,
                        $"External import name collision for '{type.Name}'. Keeping "
                            + $"'{existing.From}' ('{existing.Name}') and ignoring "
                            + $"conflicting mapping from '{type.ToDisplayString()}' to "
                            + $"'{entry.From}' ('{entry.Name}').",
                        type.Locations.FirstOrDefault()
                    )
                );
                continue;
            }

            map[type.Name] = entry;
        }

        return map;
    }

    private static void CollectPublicTopLevelTypes(INamespaceSymbol ns, List<INamedTypeSymbol> sink)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectPublicTopLevelTypes(childNs, sink);
                    break;
                case INamedTypeSymbol type
                    when type.ContainingType is null
                        && type.DeclaredAccessibility == Accessibility.Public:
                    sink.Add(type);
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
