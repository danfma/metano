namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A generated TypeScript file. FileName is a relative path including namespace folders
/// (e.g. "Orzano/Shared/Money.ts"). Namespace is the dot-separated C# namespace.
/// </summary>
public sealed record TsSourceFile(string FileName, IReadOnlyList<TsTopLevel> Statements, string Namespace = "");
