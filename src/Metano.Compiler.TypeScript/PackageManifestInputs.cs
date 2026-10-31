using Metano.Compiler;
using Metano.Compiler.TypeScript.AST;

namespace Metano;

/// <summary>
/// What a TypeScript run hands to the package.json stage
/// (<see cref="PackageJsonReconciler"/>): captured by <see cref="TypeScriptTarget.Transform"/>,
/// or rebuilt from the IR and the cache by <see cref="TypeScriptTarget.AdoptCachedOutput"/>
/// on a full cache hit, so both paths feed <see cref="PackageJsonWriter"/> the same inputs.
/// </summary>
/// <param name="SourceFiles">The generated source files. After a full cache hit these are
/// statement-less stubs carrying only the reused file names, which is all the writer
/// reads.</param>
/// <param name="PackageName">The package name from <c>[assembly: EmitPackage(name)]</c>,
/// or <see langword="null"/> when the attribute is absent.</param>
/// <param name="SubpathRoot">The validated <c>[assembly: EmitPackage(SubpathRoot = …)]</c>
/// (<see cref="PackageSubpathRoot.None"/> when absent); the <c>exports</c> keys drop that
/// namespace prefix (spec 008).</param>
/// <param name="CrossPackageDependencies">Each referenced npm package name mapped to its
/// version specifier (e.g. <c>^1.2.3</c> or <c>workspace:*</c>), merged into
/// <c>package.json#dependencies</c>.</param>
/// <param name="IsExecutable">Whether the source project is an executable
/// (ConsoleApplication). Executables are not consumed by other packages, so they get no
/// <c>exports</c>, only <c>imports</c>.</param>
public sealed record PackageManifestInputs(
    IReadOnlyList<TsSourceFile> SourceFiles,
    string? PackageName,
    PackageSubpathRoot SubpathRoot,
    IReadOnlyDictionary<string, string> CrossPackageDependencies,
    bool IsExecutable
)
{
    /// <summary>The inputs before any run: nothing to write.</summary>
    public static PackageManifestInputs None { get; } =
        new([], null, PackageSubpathRoot.None, new Dictionary<string, string>(), false);
}
