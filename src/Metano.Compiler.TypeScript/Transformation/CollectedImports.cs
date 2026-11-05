using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// What one <see cref="ImportCollector.Collect"/> pass produced for a module:
/// the merged import lines, and the <c>metano-runtime</c> helper aliases the
/// file must print its helper calls through — empty when no helper name
/// collided with a user binding or import (issue #484).
/// </summary>
/// <param name="Imports">The merged, alias-stamped import lines.</param>
/// <param name="RuntimeHelperAliases">Runtime helper name → the local <c>$rt</c> alias.</param>
internal sealed record CollectedImports(
    IReadOnlyList<TsImport> Imports,
    IReadOnlyDictionary<string, string> RuntimeHelperAliases
);
