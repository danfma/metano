namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A reference to a function exported from the same module as a type of this
/// project: the standalone helper a <c>[PlainObject]</c> instance method lowers
/// to (<c>item.GetTitle()</c> → <c>getTitle(item)</c>, exported next to
/// <c>Item</c>). The printer emits the bare <see cref="Name"/>; the
/// <see cref="OwnerTypeName"/> lets <c>ImportCollector</c> import the function
/// from the owner type's file rather than from whichever type exports a
/// function of that name. The owner is looked up by its simple name, as type
/// imports are.
/// </summary>
/// <param name="Name">The emitted function name.</param>
/// <param name="OwnerTypeName">The simple C# name of the top-level type whose module exports the function.</param>
public sealed record TsCompanionFunctionReference(string Name, string OwnerTypeName) : TsExpression;
