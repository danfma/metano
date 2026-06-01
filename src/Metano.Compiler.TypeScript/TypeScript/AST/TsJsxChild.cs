namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A child of a <see cref="TsJsxElement"/> — literal text, an embedded
/// expression (<c>{expr}</c>), or a nested element.
/// </summary>
public abstract record TsJsxChild;

/// <summary>
/// Literal text content between JSX tags.
/// </summary>
public sealed record TsJsxText(string Value) : TsJsxChild;

/// <summary>
/// An embedded-expression child: <c>{expression}</c> (including render-prop
/// lambdas).
/// </summary>
public sealed record TsJsxExpressionChild(TsExpression Expression) : TsJsxChild;

/// <summary>
/// A nested JSX element child.
/// </summary>
public sealed record TsJsxElementChild(TsJsxElement Element) : TsJsxChild;
