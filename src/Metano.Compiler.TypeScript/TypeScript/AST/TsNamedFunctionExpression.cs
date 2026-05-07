namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// JavaScript named function expression: <c>function name(params): T { … }</c>.
/// The <see cref="Name"/> is bound only inside the function body — it does
/// not pollute the enclosing scope. Used to lower an <c>[Inline]</c> method
/// whose body directly recurses on its own declaring symbol: the erased
/// declaration has no name to call from inside the lowered body, so the
/// internal name carries the recursion through the named function
/// expression form (#194).
/// </summary>
public sealed record TsNamedFunctionExpression(
    string Name,
    IReadOnlyList<TsParameter> Parameters,
    TsType? ReturnType,
    IReadOnlyList<TsStatement> Body,
    bool Async = false
) : TsExpression;
