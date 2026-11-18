namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript namespace declaration. Can contain exported functions or arbitrary
/// top-level statements (the companion of a container that does not hoist its nested
/// types, where Outer { Inner } becomes `class Outer; namespace Outer { class Inner }`).
/// With <paramref name="Declare"/> it is the erased <c>declare namespace</c> of type
/// aliases a container hoisting its nested types keeps <c>Outer.Inner</c> a type with
/// (spec 011 FR-040); it compiles to nothing.
/// </summary>
public sealed record TsNamespaceDeclaration(
    string Name,
    IReadOnlyList<TsFunction> Functions,
    bool Exported = true,
    IReadOnlyList<TsTopLevel>? Members = null,
    bool Declare = false
) : TsTopLevel;
