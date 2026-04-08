namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A TypeScript local variable declaration.
/// </summary>
/// <param name="Exported">When true, prefixes the declaration with <c>export</c>.
/// Used by <c>[ExportVarFromBody]</c> with <c>InPlace = true</c> to fold a named
/// export into a top-level variable declaration site.</param>
public sealed record TsVariableDeclaration(
    string Name,
    TsExpression Initializer,
    bool Const = true,
    bool Exported = false) : TsStatement;
