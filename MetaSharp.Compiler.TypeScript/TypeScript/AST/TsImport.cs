namespace MetaSharp.TypeScript.AST;

public sealed record TsImport(string[] Names, string From, bool TypeOnly = false) : TsTopLevel;
