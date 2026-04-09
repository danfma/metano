namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A reference to a TypeScript type used as a value position — typically the
/// receiver of a static member access (e.g., <c>Priority.High</c> where
/// <c>Priority</c> is the type, not an instance). Used by
/// <c>IdentifierHandler</c> when the resolved C# symbol is a type AND it carries a
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
