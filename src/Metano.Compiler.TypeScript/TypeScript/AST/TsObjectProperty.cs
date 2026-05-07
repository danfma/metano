namespace Metano.Compiler.TypeScript.AST;

public sealed record TsObjectProperty(string Key, TsExpression Value, bool Shorthand = false);
