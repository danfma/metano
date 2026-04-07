namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A const object declaration: export const Name = { Key: "value", ... } as const;
/// Used for StringEnum to allow accessing members as values (e.g., IssueStatus.Backlog).
/// </summary>
public sealed record TsConstObject(
    string Name,
    IReadOnlyList<(string Key, TsExpression Value)> Entries,
    bool Exported = true
) : TsTopLevel;
