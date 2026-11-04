namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A class constructor. <see cref="Accessibility"/> prefixes the
/// implementation and every overload signature: TypeScript requires them to
/// share one modifier (TS2385).
/// </summary>
public sealed record TsConstructor(
    IReadOnlyList<TsConstructorParam> Parameters,
    IReadOnlyList<TsStatement> Body,
    IReadOnlyList<TsConstructorOverload>? Overloads = null,
    TsAccessibility Accessibility = TsAccessibility.Public
);

/// <summary>
/// An overload signature for a constructor: constructor(x: number, y: number);
/// </summary>
public sealed record TsConstructorOverload(IReadOnlyList<TsConstructorParam> Parameters);
