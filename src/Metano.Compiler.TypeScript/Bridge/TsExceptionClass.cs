using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The JavaScript class a BCL exception lowers to: the name and module it is
/// imported from, and the roles its constructor takes, in order.
/// </summary>
/// <param name="Name">The exported class name.</param>
/// <param name="Origin">The module the class is imported from; <c>null</c>
/// when its <c>[ExportFromBcl]</c> entry names no module (a global).</param>
/// <param name="ConstructorRoles">The constructor's parameters by role, in
/// order. A construction passes the argument playing each role,
/// <c>undefined</c> for a role it omits ahead of one it passes, and nothing
/// after the last one; a role the class does not take is folded into the
/// message (see <see cref="IrToTsExceptionConstructionBridge"/>).</param>
/// <param name="IsRoot">Whether the class is that of <c>System.Exception</c>,
/// whose runtime tests check the built-in <c>Error</c> until
/// <c>toException</c> normalizes caught values (spec 011 FR-014, interim).</param>
public sealed record TsExceptionClass(
    string Name,
    TsTypeOrigin? Origin,
    IReadOnlyList<IrExceptionArgumentRole> ConstructorRoles,
    bool IsRoot = false
)
{
    /// <summary>
    /// The class in type position (annotations, <c>extends</c>), carrying the
    /// value its runtime tests check (<see cref="TsNamedType.InstanceofTarget"/>).
    /// </summary>
    public TsNamedType TypeReference() =>
        new(Name, Origin: Origin, InstanceofTarget: InstanceofTarget());

    /// <summary>
    /// <c>value instanceof &lt;class&gt;</c>, or <c>value instanceof Error</c>
    /// for <c>System.Exception</c>: a JavaScript error such as a
    /// <c>TypeError</c> is a C# <c>Exception</c>, as .NET's runtime errors are.
    /// </summary>
    public TsExpression InstanceofTest(TsExpression value) =>
        new TsBinaryExpression(value, "instanceof", InstanceofTarget());

    /// <summary>
    /// The value <c>instanceof</c> tests (<c>Error</c> for the root class),
    /// for a caller that checks a type at runtime through a helper rather than
    /// the operator.
    /// </summary>
    public TsExpression InstanceofTarget() => IsRoot ? new TsIdentifier("Error") : ValueReference();

    /// <summary>The class in value position (<c>new</c>, <c>instanceof</c>), carrying its import.</summary>
    public TsExpression ValueReference() =>
        Origin is null ? new TsIdentifier(Name) : new TsTypeReference(Name, Origin);

    /// <summary>Whether the constructor takes <paramref name="role"/>.</summary>
    public bool Takes(IrExceptionArgumentRole role) => ConstructorRoles.Contains(role);
}
