namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A reference to a function or variable exported from the same module as a
/// type of this project:
/// <list type="bullet">
///   <item>the standalone helper a <c>[PlainObject]</c> instance method lowers
///     to (<c>item.GetTitle()</c> → <c>getTitle(item)</c>, exported next to
///     <c>Item</c>);</item>
///   <item>a module variable of a containerless static class
///     (<c>Limits.Max</c> → <c>max</c>, exported from <c>limits.ts</c>).</item>
/// </list>
/// The printer emits the bare <see cref="Name"/>; the
/// <see cref="OwnerTypeName"/> lets <c>ImportCollector</c> import the name
/// from the owner type's file rather than from whichever type exports
/// something of that name, and keeps a local of the same name from being
/// mistaken for the export. The owner is looked up by its simple name, as
/// type imports are.
/// </summary>
/// <param name="Name">The emitted function or variable name.</param>
/// <param name="OwnerTypeName">The simple C# name of the top-level type whose module exports the name.</param>
public sealed record TsModuleExportReference(string Name, string OwnerTypeName) : TsExpression;
