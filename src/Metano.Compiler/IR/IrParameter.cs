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
/// <param name="IsOptional">Whether the parameter carries
/// <c>[Optional]</c> (from <c>Metano.Annotations.TypeScript</c>). When
/// <c>true</c>, the TS backend lowers the parameter to the
/// optional-presence form (<c>name?: T</c>) so a consumer can omit the
/// key entirely; other targets treat it as a no-op. The attribute
/// requires the parameter to already be nullable — the extractor
/// emits <c>MS0010</c> for <c>[Optional]</c> on a non-nullable
/// type.</param>
/// <param name="IsConstant">Whether the parameter carries
/// <c>[Constant]</c> (from <c>Metano.Annotations</c>). When
/// <c>true</c>, every call-site argument must be a compile-time
/// constant literal; the frontend validator emits <c>MS0014</c> for
/// violations. Downstream lowering (<c>[Emit]</c> templates,
/// <c>[Inline]</c> expansion) relies on the flag to guarantee the
/// value is reducible to a literal without a separate analyzer
/// pass.</param>
public sealed record IrParameter(
    string Name,
    IrTypeRef Type,
    bool HasDefaultValue = false,
    IrExpression? DefaultValue = null,
    bool IsParams = false,
    bool HasExplicitType = true,
    bool IsOptional = false,
    bool IsConstant = false
);
