namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A type query, <c>typeof Money.byMoneyAmount</c>: the declared type of the
/// value <paramref name="EntityName"/> names (a dotted path), such as the
/// <c>unique symbol</c> of a selector in a constructor's overload signature
/// (spec 013 FR-009).
/// </summary>
public sealed record TsTypeQuery(string EntityName) : TsType;
