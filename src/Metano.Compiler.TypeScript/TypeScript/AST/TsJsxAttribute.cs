namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A single JSX attribute: <c>name="literal"</c> or <c>name={expr}</c>.
/// </summary>
public sealed record TsJsxAttribute(string Name, TsJsxAttributeValue Value);

/// <summary>
/// The value of a JSX attribute — either a string literal or an embedded
/// expression.
/// </summary>
public abstract record TsJsxAttributeValue;

/// <summary>
/// A string-literal attribute value: <c>name="value"</c>.
/// </summary>
public sealed record TsJsxAttributeStringValue(string Value) : TsJsxAttributeValue;

/// <summary>
/// An embedded-expression attribute value: <c>name={expression}</c>.
/// </summary>
public sealed record TsJsxAttributeExpressionValue(TsExpression Expression) : TsJsxAttributeValue;
