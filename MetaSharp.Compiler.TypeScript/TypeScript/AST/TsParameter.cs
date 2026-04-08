namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A function/method/lambda parameter. <see cref="Type"/> is nullable so that lambda
/// parameters whose source-side type is <c>[NoEmit]</c> (an ambient declaration over
/// an external library shape) can be emitted without an annotation, letting TypeScript
/// infer the type from the call-site context. Non-lambda parameters always carry a
/// non-null Type — only the lambda handler currently produces null.
/// </summary>
public sealed record TsParameter(string Name, TsType? Type);
