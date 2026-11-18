using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Builds the static getters that expose a container's hoisted nested types (spec 011
/// FR-040), knowing which getter names its base classes already declare: a nested type
/// hidden with <c>new</c> (<c>public new class Inner</c>) re-declares the base getter.
/// That getter is an <c>override</c>, and its type is the base getter's intersected with
/// its own (<c>typeof Base.Inner &amp; typeof Derived$Inner</c>), since the class's static
/// side must stay assignable to the base's while the hiding type need not be.
/// </summary>
/// <param name="Names">The getter names the base classes declare.</param>
/// <param name="BaseName">The base class reference of the container, if it extends one.</param>
internal sealed record InheritedNestedGetters(IReadOnlySet<string> Names, string? BaseName)
{
    /// <summary><c>static get Name(): typeof Declared { return Declared; }</c>.</summary>
    public TsGetterMember Getter(string name, string declaredName)
    {
        var own = new TsTypeQuery(declaredName);

        if (!Names.Contains(name) || BaseName is null)
            return new TsGetterMember(
                name,
                own,
                [Return(new TsIdentifier(declaredName))],
                Static: true
            );

        TsType type = new TsIntersectionType([new TsTypeQuery($"{BaseName}.{name}"), own]);

        return new TsGetterMember(
            name,
            type,
            [Return(new TsCastExpression(new TsIdentifier(declaredName), type))],
            Static: true,
            Override: true
        );
    }

    private static TsReturnStatement Return(TsExpression value) => new(value);
}
