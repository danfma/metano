namespace Metano.Compiler.TypeScript.AST;

public sealed record TsSwitchStatement(TsExpression Discriminant, IReadOnlyList<TsSwitchCase> Cases)
    : TsStatement
{
    /// <summary>
    /// The JavaScript label on the statement (<c>dispatch: switch (…)</c>), the
    /// target of a labeled <c>break</c> (#772); <c>null</c> when it has none.
    /// </summary>
    public string? Label { get; init; }
}

/// <summary>
/// A switch case. Test is null for the default case.
/// </summary>
public sealed record TsSwitchCase(TsExpression? Test, IReadOnlyList<TsStatement> Body);
