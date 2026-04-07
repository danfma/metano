namespace MetaSharp.TypeScript.AST;

public sealed record TsSwitchStatement(
    TsExpression Discriminant,
    IReadOnlyList<TsSwitchCase> Cases
) : TsStatement;

/// <summary>
/// A switch case. Test is null for the default case.
/// </summary>
public sealed record TsSwitchCase(
    TsExpression? Test,
    IReadOnlyList<TsStatement> Body
);
