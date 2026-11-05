namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A class constructor. <see cref="Accessibility"/> prefixes the
/// implementation and every overload signature: TypeScript requires them to
/// share one modifier (TS2385). <see cref="JSDoc"/> prints ahead of a
/// constructor with no overload signatures; with overloads, each
/// <see cref="TsConstructorOverload.JSDoc"/> prints ahead of its own
/// signature and the implementation carries none.
/// </summary>
public sealed record TsConstructor(
    IReadOnlyList<TsConstructorParam> Parameters,
    IReadOnlyList<TsStatement> Body,
    IReadOnlyList<TsConstructorOverload>? Overloads = null,
    TsAccessibility Accessibility = TsAccessibility.Public,
    TsJSDoc? JSDoc = null
);

/// <summary>
/// An overload signature for a constructor: constructor(x: number, y: number);
/// <paramref name="JSDoc"/> prints ahead of the signature, taken from the C#
/// constructor it declares.
/// </summary>
public sealed record TsConstructorOverload(
    IReadOnlyList<TsConstructorParam> Parameters,
    TsJSDoc? JSDoc = null
);
