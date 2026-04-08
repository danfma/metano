namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A regular <see cref="TsStatement"/> emitted at the top level of the module file
/// (outside of any function/class). Produced by the <c>[ModuleEntryPoint]</c> lowering,
/// where the body of a marked static method is "unwrapped" so its variable declarations
/// and expression statements become module-scoped code instead of being nested inside a
/// generated function.
///
/// The printer simply emits <see cref="Inner"/> as if it were a top-level declaration;
/// the import collector descends into it via the existing statement walker.
/// </summary>
public sealed record TsTopLevelStatement(TsStatement Inner) : TsTopLevel;
