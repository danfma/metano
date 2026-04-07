namespace MetaSharp.TypeScript.AST;

public sealed record TsConstructorParam(string Name, TsType Type, bool Readonly = false, TsAccessibility Accessibility = TsAccessibility.Public, TsExpression? DefaultValue = null);
