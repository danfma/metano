namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A reference to a function the TypeScript bridge itself chose from
/// <c>metano-runtime</c> (the 64-bit conversion helpers of spec 012). The
/// printer emits the bare <see cref="Name"/>; the node tells
/// <c>ImportCollector</c> to import it from <c>metano-runtime</c>, so a user
/// function of the same name is never mistaken for the helper.
/// </summary>
/// <param name="Name">The exported name of the runtime helper.</param>
public sealed record TsRuntimeHelperReference(string Name) : TsExpression;
