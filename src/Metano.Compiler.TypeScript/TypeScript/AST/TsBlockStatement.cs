namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A nested <c>{ ... }</c> block. Scopes the <c>const</c> bindings it
/// declares to its own statements — e.g. a switch-expression arm whose
/// always-matching pattern binds a designator that the <c>when</c> guard
/// reads.
/// </summary>
public sealed record TsBlockStatement(IReadOnlyList<TsStatement> Statements) : TsStatement;
