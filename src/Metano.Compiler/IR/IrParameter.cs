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
/// <param name="IsOut">Whether this is a C# <c>out</c> parameter. Backends
/// without by-reference parameters drop it from the parameter list and
/// return its value alongside the method's result (see
/// <see cref="IrOutArgumentCall"/>).</param>
/// <param name="OutTypesByResult">For an <c>out</c> parameter of a
/// <c>bool</c>-returning method annotated with <c>[NotNullWhen]</c> or
/// <c>[MaybeNullWhen]</c>: the value's type when the method returns
/// <c>true</c> and when it returns <c>false</c>, so a backend can let its type
/// checker narrow the value on the result. <c>null</c> otherwise.</param>
public sealed record IrParameter(
    string Name,
    IrTypeRef Type,
    bool HasDefaultValue = false,
    IrExpression? DefaultValue = null,
    bool IsParams = false,
    bool HasExplicitType = true,
    bool IsOptional = false,
    bool IsConstant = false,
    bool IsOut = false,
    IrOutTypesByResult? OutTypesByResult = null
)
{
    /// <summary>
    /// The parameter type's spelling in an overload name, captured from Roslyn
    /// (spec 013 FR-002); <c>null</c> outside a dispatched overload group.
    /// </summary>
    public IrNamingSpelling? NamingSpelling { get; init; }

    /// <summary>
    /// Whether a call may omit the argument: the parameter has a C# default or
    /// carries <c>[Optional]</c>.
    /// </summary>
    public bool CanBeOmitted => HasDefaultValue || IsOptional;

    /// <summary>
    /// The element type of a <c>params T[]</c> (or span) parameter, nullable or
    /// not, whose arguments a call passes one by one; <c>null</c> for any other
    /// parameter, including a C# 13 <c>params</c> collection
    /// (<c>params List&lt;T&gt; xs</c>), which a call site constructs (spec 011
    /// contracts/runtime-collections.md § params collections).
    /// </summary>
    public IrTypeRef? ParamsElementType =>
        IsParams
            ? ((Type as IrNullableTypeRef)?.Inner ?? Type) switch
            {
                IrArrayTypeRef array => array.ElementType,
                _ => null,
            }
            : null;

    /// <summary>
    /// Whether a call passes this parameter's values one by one, so a target
    /// with rest parameters declares it as one: a <c>params</c> array or span,
    /// not a C# 13 <c>params</c> collection, which the call site constructs.
    /// </summary>
    public bool IsRestParams =>
        IsParams
        && ((Type as IrNullableTypeRef)?.Inner ?? Type)
            is not (IrCollectionTypeRef or IrIterableTypeRef);
}

/// <summary>
/// The type of an <c>out</c> value on each result of a <c>bool</c>-returning
/// method, derived from the parameter's nullability attributes:
/// <c>[NotNullWhen(true)] out T? x</c> is <c>T</c> on <c>true</c> and
/// <c>T?</c> on <c>false</c>; <c>[MaybeNullWhen(false)] out T x</c> is
/// <c>T</c> on <c>true</c> and <c>T?</c> on <c>false</c> (a value type keeps
/// <c>T</c>, since its default is not <c>null</c>).
/// </summary>
public sealed record IrOutTypesByResult(IrTypeRef WhenTrue, IrTypeRef WhenFalse);
