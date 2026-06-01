namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A top-level function declaration. <paramref name="ReturnType"/> is nullable:
/// when <c>null</c> the printer omits the <c>: T</c> annotation entirely and
/// lets TypeScript infer the return type. JSX function components use this so
/// the emitted signature reads <c>function C(props: CProps) { … }</c> (the
/// JSX return type is inferred), matching the lowering contract.
/// </summary>
public sealed record TsFunction(
    string Name,
    IReadOnlyList<TsParameter> Parameters,
    TsType? ReturnType,
    IReadOnlyList<TsStatement> Body,
    bool Exported = true,
    bool Async = false,
    bool Generator = false,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null
) : TsTopLevel;
