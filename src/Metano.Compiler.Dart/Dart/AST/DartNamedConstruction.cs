using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.AST;

/// <summary>
/// A construction through a named constructor, <c>Box&lt;Object&gt;.fromValue(5)</c>
/// (spec 013 FR-022): the constructed type, with its type arguments, then the
/// constructor's name. The IR has no node for it — a core
/// <see cref="IrNewExpression"/> names no constructor — so
/// <see cref="Bridge.DartIrRewriter"/> lowers a construction that selected a
/// dispatched overload to this Dart-only expression, which only the Dart
/// printer and import collector read.
/// </summary>
public sealed record DartNamedConstruction(
    IrTypeRef Type,
    string ConstructorName,
    IReadOnlyList<IrArgument> Arguments
) : IrExpression;
