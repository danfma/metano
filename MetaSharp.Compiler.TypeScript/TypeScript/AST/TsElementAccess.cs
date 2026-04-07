namespace MetaSharp.TypeScript.AST;

/// <summary>
/// Element access expression: obj[index]
/// </summary>
public sealed record TsElementAccess(TsExpression Object, TsExpression Index) : TsExpression;
