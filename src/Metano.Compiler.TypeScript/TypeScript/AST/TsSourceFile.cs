namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A generated TypeScript file. FileName is a relative path including namespace folders
/// (e.g. "Orzano/Shared/Money.ts"). Namespace is the dot-separated C# namespace.
/// <para>
/// <see cref="RuntimeHelperAliases"/> maps the <c>metano-runtime</c> helper names whose
/// plain import collided with a user declaration or import (issue #484) to the unique
/// alias the module binds them under (<c>isInt32</c> → <c>isInt32$rt</c>). The printer
/// resolves every <see cref="TsRuntimeHelperReference"/> through it; <c>null</c> when
/// no name collided.
/// </para>
/// <para>
/// <see cref="HasModuleSideEffects"/> is <c>true</c> when the module runs code when merely
/// imported (see <see cref="TsTopLevelStatement.RunsOnImport"/>). It is set explicitly because
/// a per-group cache hit rebuilds the file without its statements.
/// </para>
/// <para>
/// <see cref="IsPackageInternal"/> is <c>true</c> when the module declares only types that
/// are transpiled because emitted code reaches them (#770,
/// <see cref="SymbolHelper.IsReachedInternalType"/>): other modules import it, but no
/// barrel re-exports it, so it stays off the package's public surface.
/// </para>
/// </summary>
public sealed record TsSourceFile(
    string FileName,
    IReadOnlyList<TsTopLevel> Statements,
    string Namespace = "",
    IReadOnlyDictionary<string, string>? RuntimeHelperAliases = null,
    bool HasModuleSideEffects = false,
    bool IsPackageInternal = false
);
