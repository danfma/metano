namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A reference to a function the TypeScript bridge itself chose from a
/// <c>metano-runtime</c> module (the 64-bit conversion helpers of spec 012,
/// the overload-dispatcher type checks, record equality, …). The printer
/// emits the bare <see cref="Name"/> — deferred-wrapped so a colliding
/// module resolves it through the alias (issue #484) — and the node tells
/// <c>ImportCollector</c> to import it from <see cref="Module"/>, so a user
/// function of the same name is never mistaken for the helper.
/// </summary>
/// <param name="Name">The exported name of the runtime helper.</param>
/// <param name="Module">
/// The runtime module specifier the helper is imported from; the LINQ pipe
/// operators live in the tree-shakable subpath (<see cref="LinqPipeModule"/>),
/// everything else in the root barrel (<see cref="RootModule"/>).
/// </param>
public sealed record TsRuntimeHelperReference(string Name, string Module = "metano-runtime")
    : TsExpression
{
    public const string RootModule = "metano-runtime";

    public const string LinqPipeModule = "metano-runtime/system/linq";
}
