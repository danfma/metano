namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A function/method/lambda parameter. <see cref="Type"/> is nullable so that lambda
/// parameters whose source-side type is <c>[External]</c> (an ambient declaration over
/// an external library shape) can be emitted without an annotation, letting TypeScript
/// infer the type from the call-site context. Non-lambda parameters always carry a
/// non-null Type — only the lambda handler currently produces null.
/// </summary>
/// <param name="Optional">When <c>true</c> the parameter is rendered
/// with a <c>?</c> suffix (<c>name?: Type</c>) — the optional-parameter
/// form that lets the TS caller omit it. Set by
/// <c>[Optional]</c>-tagged parameters on <c>[PlainObject]</c> instance
/// methods (and any future emission site that honors the attribute).</param>
/// <param name="DefaultValue">When non-null the parameter renders as
/// <c>name: Type = expr</c>. The <c>= expr</c> form already implies
/// optionality at the call site, so it is mutually exclusive with the
/// <c>?</c> suffix — declaration-only positions (interface members,
/// abstract method signatures, overload signatures) must keep using
/// <see cref="Optional"/> instead because TypeScript forbids
/// initializers there.</param>
/// <param name="Rest">When <c>true</c> the parameter renders with a
/// <c>...</c> prefix (<c>...name: T[]</c>) — the TS rest-parameter form
/// that maps to a C# <c>params</c> argument. Must be the last parameter
/// of the signature; the type is expected to be an array type.</param>
public sealed record TsParameter(
    string Name,
    TsType? Type,
    bool Optional = false,
    TsExpression? DefaultValue = null,
    bool Rest = false
);
