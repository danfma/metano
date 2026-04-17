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

    /// <summary>
    /// Compilation produced by the most recent <see cref="ExtractAsync"/>
    /// call, or <see langword="null"/> if loading failed. Exposed as a
    /// transitional escape hatch so <see cref="TranspilerHost"/> can pass
    /// the Roslyn <see cref="Compilation"/> on to the active
    /// <see cref="ITranspilerTarget"/> while the targets still drive
    /// their own extraction. Will be removed once every call site
    /// consumes the <see cref="IrCompilation"/> directly.
    /// </summary>
    public Compilation? LoadedCompilation { get; private set; }

    /// <summary>
    /// Count of Roslyn-level errors surfaced during the most recent
    /// <see cref="ExtractAsync"/> call. Zero when loading succeeded.
    /// </summary>
    public int LoadErrorCount { get; private set; }

    public async Task<IrCompilation> ExtractAsync(
        string projectPath,
        CancellationToken ct = default
    )
    {
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);

        var (compilation, errorCount) = await LoadCompilationAsync(projectPath, ct);
        LoadedCompilation = compilation;
        LoadErrorCount = errorCount;

        // Fields beyond AssemblyName stay empty during the shell phase.
        // Downstream targets fall back to `LoadedCompilation` for
        // discovery / extraction until the follow-up migration wires
        // them onto `IrCompilation`.
        return new IrCompilation(
            AssemblyName: compilation?.AssemblyName ?? assemblyName,
            PackageName: null,
            AssemblyWideTranspile: false,
            Modules: Array.Empty<IrModule>(),
            ReferencedModules: Array.Empty<IrModule>(),
            CrossAssemblyOrigins: new Dictionary<string, IrTypeOrigin>(StringComparer.Ordinal),
            ExternalImports: new Dictionary<string, IrExternalImport>(StringComparer.Ordinal),
            BclExports: new Dictionary<string, IrBclExport>(StringComparer.Ordinal),
            AssembliesNeedingEmitPackage: new HashSet<string>(StringComparer.Ordinal),
            Diagnostics: []
        );
    }

    /// <summary>
    /// Opens the project via <see cref="MSBuildWorkspace"/> and returns
    /// the resulting <see cref="Compilation"/>. Mirrors the previous
    /// <c>TranspilerHost.LoadCompilationAsync</c>: progress and errors
    /// go to stdout/stderr; a null result signals "do not proceed" and
    /// the error count propagates to the CLI exit code.
    /// </summary>
    private static async Task<(Compilation? Compilation, int ErrorCount)> LoadCompilationAsync(
        string projectPath,
        CancellationToken ct
    )
    {
        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine($"Project not found: {projectPath}");
            return (null, 1);
        }

        Console.WriteLine($"Metano: Loading project {Path.GetFileName(projectPath)}...");

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"  Workspace error: {e.Diagnostic.Message}");
        });

        Console.WriteLine("  Opening MSBuild project...");
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);

        Console.WriteLine("  Project loaded.");
        Console.WriteLine("  Creating Roslyn compilation...");
        var compilation = await project.GetCompilationAsync(ct);

        Console.WriteLine("  Compilation created.");

        if (compilation is null)
        {
            Console.Error.WriteLine("Failed to compile project.");
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
            return (null, roslynErrors.Count);
        }

        return (compilation, 0);
    }
}
