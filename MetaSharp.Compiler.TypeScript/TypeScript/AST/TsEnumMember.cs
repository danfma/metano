namespace MetaSharp.TypeScript.AST;

public sealed record TsEnumMember(string Name, TsExpression? Value = null);
