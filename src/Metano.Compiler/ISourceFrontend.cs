using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Implemented by each source-language frontend (C# via Roslyn today,
/// potentially others in the future). Given a project entry point, the
/// frontend loads the source, discovers transpilable declarations, runs
/// the semantic extraction pipeline, and produces a target-agnostic
/// <see cref="IrCompilation"/> every backend can consume.
/// <para>
/// A frontend is stateless across invocations — one call per transpile
/// run. The resulting <see cref="IrCompilation"/> carries all state the
/// backend needs; no shared context reaches over.
/// </para>
/// </summary>
public interface ISourceFrontend
{
    /// <summary>Short identifier for CLI / log messages (e.g.,
    /// <c>"csharp"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Roslyn <see cref="Compilation"/> produced by the most recent
    /// <see cref="ExtractAsync"/> call, or <see langword="null"/> if
    /// loading failed.
    /// <para>
    /// <b>Transitional escape hatch.</b> Exposed so
    /// <see cref="TranspilerHost"/> can hand the Roslyn compilation to
    /// the active <see cref="ITranspilerTarget"/> while the targets still
    /// drive their own discovery + extraction. Will be removed in B.3
    /// once every call site consumes the <see cref="IrCompilation"/>
    /// directly. Frontends with no Roslyn backing return
    /// <see langword="null"/>.
    /// </para>
    /// </summary>
    Compilation? LoadedCompilation { get; }

    /// <summary>
    /// Count of language-level errors surfaced during the most recent
    /// <see cref="ExtractAsync"/> call (Roslyn diagnostics for the C#
    /// frontend). Zero when loading succeeded.
    /// <para>
    /// <b>Transitional escape hatch.</b> Mirrors
    /// <see cref="LoadedCompilation"/> — used by
    /// <see cref="TranspilerHost"/> as the CLI exit code while
    /// frontend-level failures are also reported via
    /// <see cref="IrCompilation.Diagnostics"/>. Will be removed in B.3.
    /// </para>
    /// </summary>
    int LoadErrorCount { get; }

    /// <summary>Asynchronously loads + extracts the project at
    /// <paramref name="projectPath"/>. Never returns <see langword="null"/>
    /// — frontend-level failures (project not found, compile errors,
    /// malformed attributes) surface via
    /// <see cref="IrCompilation.Diagnostics"/> with
    /// <see cref="MetanoDiagnosticSeverity.Error"/>, and the caller
    /// decides whether to proceed with the (possibly empty) module
    /// list.</summary>
    /// <param name="projectPath">Path to the entry source artifact (e.g.,
    /// a <c>.csproj</c> for the C# frontend).</param>
    /// <param name="target">The backend the extraction is being performed
    /// for. Drives target-specific resolution (per-target <c>[Name]</c>
    /// aliases, per-target <c>[NoEmit]</c> filters) so the resulting IR
    /// already carries the names and emission decisions the backend will
    /// use. Defaults to <see cref="TargetLanguage.TypeScript"/> for
    /// backwards compatibility with tests and any callers that predate the
    /// parameter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IrCompilation> ExtractAsync(
        string projectPath,
        TargetLanguage target = TargetLanguage.TypeScript,
        CancellationToken ct = default
    );
}
