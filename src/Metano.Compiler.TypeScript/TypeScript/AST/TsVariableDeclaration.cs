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
public sealed record TsVariableDeclaration(
    string Name,
    TsExpression? Initializer,
    bool Const = true,
    bool Exported = false,
    TsType? Type = null
) : TsStatement;
