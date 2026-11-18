using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Builds the static getters that expose a container's hoisted nested types (spec 011
/// FR-040), knowing which getter names its base classes already declare: a nested type
/// hidden with <c>new</c> (<c>public new class Inner : Base.Inner</c>) re-declares the
/// base getter, so it is an <c>override</c>. The getter keeps its own type: when the
/// hiding type extends the hidden one, the derived static side still extends the
/// base's; when it does not, TypeScript has no sound static side for it and reports
/// TS2417 on the derived class, a loud error rather than a cast that hides a runtime
/// mismatch.
/// </summary>
/// <param name="Names">The getter names the base classes declare.</param>
internal sealed record InheritedNestedGetters(IReadOnlySet<string> Names)
{
    /// <summary><c>static get Name(): typeof Declared { return Declared; }</c>.</summary>
    public TsGetterMember Getter(string name, string declaredName) =>
        new(
            name,
            new TsTypeQuery(declaredName),
            [new TsReturnStatement(new TsIdentifier(declaredName))],
            Static: true,
            Override: Names.Contains(name)
        );
}
