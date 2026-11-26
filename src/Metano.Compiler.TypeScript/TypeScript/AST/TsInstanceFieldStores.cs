namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// Where a constructor path stores the instance fields (W10-1): right after
/// <c>super(...)</c> and the type-argument slot, in the constructor that runs
/// the C# initializers (not one chaining with <c>: this(...)</c>). Carries
/// the values that path binds: <see cref="Promoted"/> members (stored first,
/// in parameter order) and <see cref="Captured"/> parameter fields (stored in
/// their declaration position). A placeholder the class emitter replaces with
/// the stores before printing (<c>InstanceFieldStoreLowering</c>); the printer
/// rejects it.
/// </summary>
public sealed record TsInstanceFieldStores(
    IReadOnlyList<TsFieldStoreBinding> Promoted,
    IReadOnlyList<TsFieldStoreBinding> Captured
) : TsStatement;

/// <summary>The value a constructor path stores in the field <paramref name="Member"/>.</summary>
public sealed record TsFieldStoreBinding(string Member, TsExpression Value);
