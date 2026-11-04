namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A constructor parameter, optionally promoted to a parameter property
/// via <see cref="Accessibility"/> / <see cref="Readonly"/>.
/// <see cref="Rest"/> renders the <c>...</c> prefix and is mutually
/// exclusive with the parameter-property modifiers — TypeScript forbids
/// both shapes on the same parameter. <see cref="Optional"/> renders the
/// <c>?</c> suffix (<c>name?: T</c>) a declaration-only overload signature
/// uses for a parameter with a default, since TypeScript forbids an
/// initializer there.
/// </summary>
public sealed record TsConstructorParam(
    string Name,
    TsType Type,
    bool Readonly = false,
    TsAccessibility Accessibility = TsAccessibility.Public,
    TsExpression? DefaultValue = null,
    bool Rest = false,
    bool Optional = false
);
