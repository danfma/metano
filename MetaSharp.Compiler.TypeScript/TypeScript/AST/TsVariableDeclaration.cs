namespace MetaSharp.TypeScript.AST;

public sealed record TsVariableDeclaration(string Name, TsExpression Initializer, bool Const = true)
    : TsStatement;
