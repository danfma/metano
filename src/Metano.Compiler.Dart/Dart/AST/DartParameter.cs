namespace Metano.Dart.AST;

/// <summary>A method or function parameter.</summary>
/// <param name="DefaultValue">Optional default expression. When present and
/// <see cref="IsNamed"/> is false, the printer emits the parameter inside the
/// Dart optional positional block (<c>[…]</c>) with the default appended as
/// <c>= expr</c>.</param>
/// <param name="IsNamed">When true, the printer groups the parameter into Dart's
/// named parameter block (<c>{…}</c>). Named parameters without a default are
/// either nullable (the inner <see cref="Type"/> is nullable) or must be tagged
/// <see cref="IsRequired"/>.</param>
/// <param name="IsRequired">Only meaningful when <see cref="IsNamed"/> is true.
/// Forces callers to pass the argument even though it lives in the <c>{…}</c>
/// block; the printer emits the <c>required</c> modifier.</param>
public sealed record DartParameter(
    string Name,
    DartType Type,
    Metano.Compiler.IR.IrExpression? DefaultValue = null,
    bool IsNamed = false,
    bool IsRequired = false
);
