namespace Metano.Compiler.IR;

/// <summary>
/// A parameter in a method, lambda, or function type signature.
/// Names stay in their original C# casing — each target backend applies its own naming policy.
/// </summary>
/// <param name="Name">Parameter name in original C# casing.</param>
/// <param name="Type">Semantic type reference.</param>
/// <param name="HasDefaultValue">Whether this parameter has a default value in C#.</param>
/// <param name="DefaultValue">The parsed default expression when
/// <paramref name="HasDefaultValue"/> is true; <c>null</c> when the extractor didn't
/// have the syntax context to parse it (e.g., referenced-assembly symbols without
/// syntax trees). Backends that cannot reproduce the default (because the
/// expression uses features outside the covered IR subset) may fall back to
/// rendering the parameter as required.</param>
/// <param name="IsParams">Whether this is a <c>params</c> (variadic) parameter.</param>
public sealed record IrParameter(
    string Name,
    IrTypeRef Type,
    bool HasDefaultValue = false,
    IrExpression? DefaultValue = null,
    bool IsParams = false,
    bool HasExplicitType = true
);
