namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript conditional type: <c>Check extends Extends ? WhenTrue : WhenFalse</c>.
/// </summary>
public sealed record TsConditionalType(
    TsType Check,
    TsType Extends,
    TsType WhenTrue,
    TsType WhenFalse
) : TsType;
