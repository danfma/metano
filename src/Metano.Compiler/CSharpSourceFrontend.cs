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

        // Fields beyond AssemblyName / Diagnostics stay empty during the
        // shell phase. Downstream targets fall back to `LoadedCompilation`
        // for discovery / extraction until the follow-up migration wires
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
            Diagnostics: diagnostics
        );
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
