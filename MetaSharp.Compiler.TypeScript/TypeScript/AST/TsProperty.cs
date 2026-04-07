namespace MetaSharp.TypeScript.AST;

public sealed record TsProperty(string Name, TsType Type, bool Readonly = false, TsAccessibility Accessibility = TsAccessibility.Public);
