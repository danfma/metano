namespace Metano.Compiler.TypeScript.AST;

public sealed record TsArrowFunction(
    IReadOnlyList<TsParameter> Parameters,
    IReadOnlyList<TsStatement> Body,
    bool Async = false
) : TsExpression
{
    /// <summary>
    /// The declared return type (<c>(v: unknown): v is X =&gt; …</c>);
    /// <c>null</c> to let TypeScript infer it, as every lowered C# lambda does.
    /// </summary>
    public TsType? ReturnType { get; init; }
}
