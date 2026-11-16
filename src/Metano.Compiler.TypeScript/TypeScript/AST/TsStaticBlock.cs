namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A class static initialization block: <c>static { … }</c> (ES2022). It runs
/// once when the class is defined, after the static fields declared before it.
/// </summary>
public sealed record TsStaticBlock(IReadOnlyList<TsStatement> Body) : TsClassMember;
