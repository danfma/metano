namespace MetaSharp.TypeScript.AST;

public sealed record TsTypeAlias(string Name, TsType Type, bool Exported = true) : TsTopLevel;
