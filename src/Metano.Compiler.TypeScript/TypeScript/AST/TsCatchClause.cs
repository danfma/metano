namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// The <c>catch</c> block of a <see cref="TsTryStatement"/>: the parameter that binds
/// the caught value and the statements that run inside that binding.
/// </summary>
public sealed record TsCatchClause(string Parameter, IReadOnlyList<TsStatement> Body);
