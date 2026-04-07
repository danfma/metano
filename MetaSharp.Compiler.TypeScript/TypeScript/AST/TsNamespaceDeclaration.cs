namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A TypeScript namespace declaration. Can contain exported functions (used for
/// InlineWrapper companions) or arbitrary top-level statements (used for nested type
/// companions, where Outer { Inner } becomes `class Outer; namespace Outer { class Inner }`).
/// </summary>
public sealed record TsNamespaceDeclaration(
    string Name,
    IReadOnlyList<TsFunction> Functions,
    bool Exported = true,
    IReadOnlyList<TsTopLevel>? Members = null
) : TsTopLevel;
