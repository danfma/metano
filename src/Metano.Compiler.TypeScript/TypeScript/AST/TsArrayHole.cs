namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// An empty slot of an array literal or array pattern (<c>[, v]</c>): skips
/// the position without binding or producing a value.
/// </summary>
public sealed record TsArrayHole : TsExpression;
