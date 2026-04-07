namespace MetaSharp.TypeScript.AST;

/// <summary>
/// Represents a re-export statement: export { Name1, Name2 } from "./Module";
/// When Names contains "*", generates: export * from "./Module";
/// </summary>
public sealed record TsReExport(string[] Names, string From, bool TypeOnly = false) : TsTopLevel;
