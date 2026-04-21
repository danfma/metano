using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Implemented by each language-specific backend (TypeScript, Dart, Kotlin, …).
/// The host (<see cref="TranspilerHost"/>) takes care of opening the source
/// project, running the active <see cref="ISourceFrontend"/>, and writing files
/// to disk; the target receives the shared <see cref="IrCompilation"/> (the
/// canonical, source-language-agnostic input) and — transitionally — the
/// Roslyn <see cref="Compilation"/> when the active frontend is the C# one,
/// so legacy code paths that have not yet migrated onto the IR can keep
/// running.
/// </summary>
public interface ITranspilerTarget
{
    /// <summary>Short name used in CLI/log messages (e.g., "typescript", "dart").</summary>
    string Name { get; }

    /// <summary>
    /// The <see cref="TargetLanguage"/> this backend emits for. The host
    /// passes it to the active <see cref="ISourceFrontend"/> so
    /// target-specific resolution (per-target <c>[Name]</c>, per-target
    /// <c>[NoEmit]</c>, …) happens once during extraction instead of being
    /// duplicated inside every target. Default keeps
    /// <see cref="TargetLanguage.TypeScript"/> so third-party targets that
    /// predate this member keep compiling until they opt in; in-tree
    /// targets override with their actual language.
    /// </summary>
    TargetLanguage Language => TargetLanguage.TypeScript;

    /// <summary>
    /// Produces the set of files (plus diagnostics) the host should emit. Targets
    /// SHOULD prefer the data on <paramref name="ir"/> over <paramref name="compilation"/>
    /// for anything the active frontend already populates; the Roslyn compilation is
    /// exposed only as a transitional escape hatch and will be removed once the active
    /// targets stop reading from it. Implementations must NOT perform file I/O — the
    /// host writes the returned files.
    /// </summary>
    /// <param name="ir">Shared IR built by the active <see cref="ISourceFrontend"/>.</param>
    /// <param name="compilation">Roslyn compilation, if the active frontend produced one.
    /// <see langword="null"/> for frontends with no Roslyn backing — IR-only targets can
    /// ignore the parameter entirely; Roslyn-dependent targets should surface a clear
    /// diagnostic when it is unavailable.</param>
    TargetOutput Transform(IrCompilation ir, Compilation? compilation);
}
