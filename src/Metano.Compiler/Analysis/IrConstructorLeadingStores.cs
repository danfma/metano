using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The instance storage members each declared constructor of a class assigns
/// before anything can observe the instance, so a target may let that store
/// replace the one of the member's C# default (#787 N11).
/// <para>
/// A constructor's run is the leading sequence of plain stores
/// (<c>this.f = value</c> / <c>f = value</c>) in its body to the class's own
/// fields and auto-properties whose values never reference the instance (no
/// <c>this</c> or <c>base</c>, also inside a lambda): until the run ends, no
/// code can read a member it assigns. A constructor that chains to another
/// one (<c>: this(...)</c>) has an empty run, since the constructor it chains
/// to runs first. A class without a declared constructor has no runs.
/// </para>
/// </summary>
public static class IrConstructorLeadingStores
{
    /// <summary>
    /// One run per declared constructor of <paramref name="type"/>, in
    /// declaration order: the IR names of the members it stores, each once,
    /// in the order of their first store.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> RunsOf(IrClassDeclaration type)
    {
        if (type.Constructor is not { } constructor)
            return [];

        var storage = PlainStorageOf(type);

        return constructor
            .DeclarationsInOrder.Select(declaration => LeadingStoresOf(declaration, storage))
            .ToList();
    }

    /// <summary>
    /// The instance members a store writes directly: fields and
    /// auto-properties whose accessors run no code (not abstract, virtual,
    /// overriding, or backed per class).
    /// </summary>
    private static HashSet<string> PlainStorageOf(IrClassDeclaration type) =>
        (type.Members ?? []).Where(IsPlainStorage).Select(member => member.Name).ToHashSet();

    private static bool IsPlainStorage(IrMemberDeclaration member) =>
        member switch
        {
            IrFieldDeclaration { IsStatic: false, IsConstant: false } => true,
            IrPropertyDeclaration { IsStatic: false, Semantics: { } semantics } =>
                IsAutoPropertyWithoutDispatch(semantics),
            _ => false,
        };

    private static bool IsAutoPropertyWithoutDispatch(IrPropertySemantics semantics) =>
        semantics
            is {
                HasGetterBody: false,
                HasSetterBody: false,
                IsAbstract: false,
                IsVirtual: false,
                IsOverride: false,
                OwnsPerClassStorage: false,
                EmitsPerClassStorage: false,
                IsIndexer: false,
                InheritedAccessor: IrInheritedAccessor.None,
            };

    private static IReadOnlyList<string> LeadingStoresOf(
        IrConstructorDeclaration declaration,
        HashSet<string> storage
    )
    {
        var stored = new List<string>();

        if (declaration.ThisChain is not null)
            return stored;

        foreach (var statement in declaration.Body ?? [])
        {
            if (StoredMemberOf(statement, storage) is not { } member)
                break;

            if (!stored.Contains(member))
                stored.Add(member);
        }

        return stored;
    }

    /// <summary>
    /// The member <paramref name="statement"/> stores when it is a plain store
    /// to one of the <paramref name="storage"/> members whose value never
    /// references the instance; <c>null</c> otherwise.
    /// </summary>
    private static string? StoredMemberOf(IrStatement statement, HashSet<string> storage) =>
        statement
            is IrExpressionStatement
            {
                Expression: IrBinaryExpression
                {
                    Operator: IrBinaryOp.Assign,
                    Left: IrMemberAccess { Target: IrThisExpression, MemberName: var member },
                    Right: var value,
                },
            }
        && storage.Contains(member)
        && !ReferencesInstance(value)
            ? member
            : null;

    private static bool ReferencesInstance(IrExpression expression) =>
        expression is IrThisExpression or IrBaseExpression
        || IrNodeChildren.ExpressionsOf(expression).Any(ReferencesInstance)
        || IrNodeChildren.StatementsOf(expression).Any(ReferencesInstance);

    private static bool ReferencesInstance(IrStatement statement) =>
        IrNodeChildren.ExpressionsOf(statement).Any(ReferencesInstance)
        || IrNodeChildren.StatementsOf(statement).Any(ReferencesInstance);
}
