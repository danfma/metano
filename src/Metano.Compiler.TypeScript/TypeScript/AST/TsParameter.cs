namespace Metano.TypeScript.AST;

/// <summary>
/// A function/method/lambda parameter. <see cref="Type"/> is nullable so that lambda
/// parameters whose source-side type is <c>[NoEmit]</c> (an ambient declaration over
/// an external library shape) can be emitted without an annotation, letting TypeScript
/// infer the type from the call-site context. Non-lambda parameters always carry a
/// non-null Type — only the lambda handler currently produces null.
/// </summary>
/// <param name="Optional">When <c>true</c> the parameter is rendered
/// with a <c>?</c> suffix (<c>name?: Type</c>) — the optional-parameter
/// form that lets the TS caller omit it. Set by
/// <c>[Optional]</c>-tagged parameters on <c>[PlainObject]</c> instance
/// methods (and any future emission site that honors the attribute).</param>
public sealed record TsParameter(string Name, TsType? Type, bool Optional = false);
