using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Implemented by each language-specific backend (TypeScript, Dart, Kotlin, …).
/// The host (<see cref="TranspilerHost"/>) takes care of opening the source project,
/// running the C# frontend, and writing files to disk; the target receives both the
/// shared <see cref="IrCompilation"/> (the canonical, source-language-agnostic input)
/// and — transitionally — the underlying Roslyn <see cref="Compilation"/> so it can
/// keep using bits of the legacy code path that have not yet migrated onto the IR.
/// </summary>
public interface ITranspilerTarget
{
    /// <summary>Short name used in CLI/log messages (e.g., "typescript", "dart").</summary>
    string Name { get; }

    /// <summary>
    /// Produces the set of files (plus diagnostics) the host should emit. Targets
    /// SHOULD prefer the data on <paramref name="ir"/> over <paramref name="compilation"/>
    /// for anything the frontend already populates; the Roslyn compilation is exposed as
    /// a transitional escape hatch and will be removed once the active targets stop
    /// reading from it. Implementations must NOT perform file I/O — the host writes
    /// the returned files.
    /// </summary>
    /// <param name="ir">Shared IR built by the active <see cref="ISourceFrontend"/>.</param>
    /// <param name="compilation">Roslyn compilation produced by the C# frontend.
    /// Transitional escape hatch for legacy code paths.</param>
    TargetOutput Transform(IrCompilation ir, Compilation compilation);
}
