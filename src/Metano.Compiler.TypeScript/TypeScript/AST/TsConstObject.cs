namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A const object declaration: <c>export const Name = { Key: value, ... } as const;</c>.
/// Holds the members of an enum and of a branded companion as values
/// (<c>IssueStatus.Backlog</c>, <c>Money.create(x)</c>). With
/// <paramref name="Shape"/>, the constant is annotated with that written type instead of
/// <c>as const</c> (<c>export const Name: { … } = { … };</c>): <c>stripInternal</c> drops
/// only the members of a type annotation, never those of an inferred or asserted type.
/// </summary>
public sealed record TsConstObject(
    string Name,
    IReadOnlyList<TsConstObjectEntry> Entries,
    bool Exported = true,
    TsObjectType? Shape = null
) : TsTopLevel;

/// <summary>One <c>Key: value</c> entry of a <see cref="TsConstObject"/>.</summary>
public sealed record TsConstObjectEntry(string Key, TsExpression Value);
