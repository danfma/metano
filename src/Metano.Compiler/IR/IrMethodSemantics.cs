namespace Metano.Compiler.IR;

/// <summary>
/// Describes the semantic nature of a method, independent of how any target renders it.
/// </summary>
/// <param name="IsAsync">The method uses <c>async</c>/<c>await</c>.</param>
/// <param name="IsGenerator">The method uses <c>yield</c> (iterator blocks).</param>
/// <param name="IsExtension">C# extension method — first parameter is <c>this</c>.</param>
/// <param name="IsOperator">User-defined operator overload.</param>
/// <param name="OperatorKind">Operator kind when <see cref="IsOperator"/> is true
/// (e.g., <c>"Addition"</c>, <c>"Equality"</c>, <c>"Implicit"</c>).</param>
/// <param name="IsSynthesized">Compiler-generated method (e.g., record <c>Equals</c>,
/// <c>GetHashCode</c>, <c>With</c>).</param>
/// <param name="HasDefaultImplementation">The method carries an executable body even though
/// it is declared on an interface (C# 8+ default interface methods). Backends that cannot
/// render default implementations should reject or ignore the member explicitly.</param>
/// <param name="IsAbstract">Declared <c>abstract</c>.</param>
/// <param name="IsVirtual">Declared <c>virtual</c>.</param>
/// <param name="IsOverride">Declared <c>override</c>.</param>
/// <param name="IsSealed">Declared <c>sealed override</c>.</param>
/// <param name="IsEmitTemplate">The method is an inline emit template (<c>[Emit]</c>):
/// it has no real body, only a textual template lowered at every call site. Backends
/// must not emit a declaration for it — it exists in the IR purely so the call-site
/// extractor can resolve the template through the symbol's attributes.</param>
public sealed record IrMethodSemantics(
    bool IsAsync = false,
    bool IsGenerator = false,
    bool IsExtension = false,
    bool IsOperator = false,
    string? OperatorKind = null,
    bool IsSynthesized = false,
    bool HasDefaultImplementation = false,
    bool IsAbstract = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    bool IsSealed = false,
    bool IsEmitTemplate = false
);
