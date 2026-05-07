namespace Metano.Compiler.TypeScript.AST;

public sealed record TsObjectLiteral(IReadOnlyList<TsObjectProperty> Properties) : TsExpression;
