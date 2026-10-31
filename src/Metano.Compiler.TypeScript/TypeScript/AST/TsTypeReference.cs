namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A reference to a TypeScript type used as a value position — the receiver of a
/// static member access (e.g., <c>Priority.High</c> where <c>Priority</c> is the
/// type, not an instance), a <c>new</c> target, or an <c>instanceof</c> operand.
/// Produced by <c>IrToTsExpressionBridge</c> when the referenced type carries a
/// cross-package <see cref="TsTypeOrigin"/>; the origin lets
/// <c>ImportCollector</c> emit the corresponding import statement even though the
/// type appears in expression position rather than as a parameter / return type
/// (which would naturally flow through the existing <see cref="TsNamedType.Origin"/>
/// path).
///
/// The printer emits this as the bare <see cref="Name"/> — it's syntactically
/// indistinguishable from a <see cref="TsIdentifier"/> at the call site, the difference
/// is purely metadata for the import collector.
/// </summary>
public sealed record TsTypeReference(string Name, TsTypeOrigin Origin) : TsExpression;
