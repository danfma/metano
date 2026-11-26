using System.Text.RegularExpressions;
using Metano.Compiler;

namespace Metano;

/// <summary>
/// The optional <c>tslib</c> peer dependency of a generated package that declares a
/// TypeScript <c>using</c> (FR-111, decision W11-1a). Below target <c>ESNext</c>, TypeScript
/// downlevels <c>using</c> to its <c>__addDisposableResource</c> / <c>__disposeResources</c>
/// helpers; a consumer that compiles with <c>importHelpers</c> imports them from
/// <c>tslib</c> instead of a copy in every module, which bundlers cannot drop. The peer is
/// optional (<c>peerDependenciesMeta</c>), since a consumer targeting <c>ESNext</c>, or
/// compiling without <c>importHelpers</c>, does not need it.
/// </summary>
public static partial class DisposalHelpersPeerDependency
{
    public const string PackageName = "tslib";

    /// <summary>The first <c>tslib</c> release with the disposal helpers.</summary>
    public const string Specifier = "^2.6.0";

    /// <summary>
    /// True when any generated file declares a <c>using</c> local. Reads the printed
    /// contents, the one input every path has (see
    /// <see cref="RuntimePeerDependency.IsImportedBy"/>), relying on the printer putting each
    /// declaration at the start of its own line.
    /// </summary>
    public static bool IsNeededBy(IEnumerable<GeneratedFile> files) =>
        files.Any(file => UsingDeclaration().IsMatch(file.Content));

    [GeneratedRegex(@"^[ \t]*using [A-Za-z_$][\w$]*(?:: [^=\n]+)? = ", RegexOptions.Multiline)]
    private static partial Regex UsingDeclaration();
}
