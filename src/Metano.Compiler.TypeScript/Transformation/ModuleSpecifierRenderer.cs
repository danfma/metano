using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The single decision point for the text of a module specifier (spec 004
/// Amendment A1). A <see cref="TsPackageModule"/> prints verbatim; a
/// <see cref="TsLocalModule"/> is rendered relative to the file key of the
/// importing file and names a concrete file with a <c>.js</c> extension
/// (<c>./issue-id.js</c>, <c>../../shared-kernel/user-id.js</c>, a barrel as
/// <c>./app/domain/index.js</c>; FR-011), so the output resolves under plain Node
/// ESM, Bun, TypeScript <c>nodenext</c>/<c>bundler</c> and Vite with no resolution
/// map. Every import and re-export the <see cref="Printer"/> writes, and the order
/// <see cref="ImportCollector"/> sorts local imports in, go through
/// <see cref="Render"/>; another suffix (spec 004 FR-016, a future option) would
/// only change <see cref="Extension"/>.
/// </summary>
public sealed class ModuleSpecifierRenderer
{
    /// <summary>The suffix of every local specifier, for <c>.ts</c> and <c>.tsx</c> targets alike.</summary>
    private const string Extension = ".js";

    /// <summary>The one renderer every run shares.</summary>
    public static readonly ModuleSpecifierRenderer Default = new();

    /// <summary>The specifier text for <paramref name="module"/> imported from <paramref name="importerFileKey"/>.</summary>
    public string Render(TsModuleSpecifier module, string importerFileKey) =>
        module switch
        {
            TsPackageModule package => package.Specifier,
            TsLocalModule local => RelativePath(importerFileKey, local.FileKey) + Extension,
            _ => throw new InvalidOperationException(
                $"Unhandled module specifier '{module.GetType().Name}'."
            ),
        };

    /// <summary>
    /// The path from the directory of <paramref name="importerFileKey"/> to
    /// <paramref name="targetKey"/>: <c>./</c> when the target sits in or below that
    /// directory, one <c>../</c> per directory to climb otherwise.
    /// </summary>
    private static string RelativePath(string importerFileKey, string targetKey)
    {
        var importerDirectory = importerFileKey.Split('/')[..^1];
        var target = targetKey.Split('/');
        var shared = SharedDirectoryCount(importerDirectory, target);
        var climb = importerDirectory.Length - shared;
        var prefix = climb == 0 ? "./" : string.Concat(Enumerable.Repeat("../", climb));

        return prefix + string.Join('/', target[shared..]);
    }

    private static int SharedDirectoryCount(string[] importerDirectory, string[] target) =>
        importerDirectory
            .Zip(target[..^1])
            .TakeWhile(segments => segments.First == segments.Second)
            .Count();
}
