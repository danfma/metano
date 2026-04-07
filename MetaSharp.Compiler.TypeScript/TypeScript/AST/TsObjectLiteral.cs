namespace MetaSharp.TypeScript.AST;

public sealed record TsObjectLiteral(IReadOnlyList<TsObjectProperty> Properties) : TsExpression;
