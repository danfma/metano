namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript local variable declaration.
/// </summary>
/// <param name="Initializer">The initial value, or <c>null</c> for a
/// <c>let</c> declared ahead of its first assignment (e.g. a hoisted
/// <c>out var</c> local), which then needs a <paramref name="Type"/>.</param>
/// <param name="Exported">When true, prefixes the declaration with <c>export</c>.
/// Used by <c>[ExportVarFromBody]</c> with <c>InPlace = true</c> to fold a named
/// export into a top-level variable declaration site.</param>
/// <param name="Type">An explicit type annotation (<c>let x: T</c>); <c>null</c>
/// lets TypeScript infer it from the initializer.</param>
/// <param name="DefinitelyAssigned">Prints the definite-assignment assertion
/// (<c>let x!: T</c>) for a local without an initializer that is assigned where
/// TypeScript's flow analysis cannot see it (inside a closure).</param>
public sealed record TsVariableDeclaration(
    string Name,
    TsExpression? Initializer,
    bool Const = true,
    bool Exported = false,
    TsType? Type = null,
    bool DefinitelyAssigned = false,
    TsJSDoc? JSDoc = null
) : TsStatement
{
    /// <summary>
    /// The declaration holds a C# static field's state (a <c>[NoContainer]</c> or
    /// <c>[ExportedAsModule]</c> module variable) whose initializer has no observable
    /// effect (a C# default or a side-effect-free value): a call or <c>new</c>
    /// initializer prints <c>/* @__PURE__ */</c>, so a bundle may drop it with the
    /// variable (spec 011 FR-043). Never set for a <c>[ModuleEntryPoint]</c>
    /// statement.
    /// </summary>
    public bool InitializesStaticState { get; init; }
}
