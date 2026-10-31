namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// Represents a TypeScript tuple type: <c>[T1, T2, ...]</c>, or with
/// <paramref name="Labels"/> and <paramref name="IsReadonly"/>
/// <c>readonly [found: true, value: V]</c>.
/// </summary>
/// <param name="Labels">One element label per element, or <c>null</c> for an
/// unlabeled tuple.</param>
public sealed record TsTupleType(
    IReadOnlyList<TsType> Elements,
    IReadOnlyList<string>? Labels = null,
    bool IsReadonly = false
) : TsType;
