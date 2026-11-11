using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The JavaScript class a BCL exception lowers to: the name and module it is
/// imported from, and the roles its constructor takes, in order.
/// </summary>
/// <param name="Name">The exported class name.</param>
/// <param name="Origin">The module the class is imported from; <c>null</c> for
/// the built-in <c>Error</c>, which needs no import.</param>
/// <param name="ConstructorRoles">The constructor's parameters by role, in
/// order. A construction passes the argument playing each role, <c>null</c> for
/// a role it omits ahead of one it passes, and nothing after the last one.</param>
/// <param name="TakesInnerExceptionAsCause">Whether the
/// <see cref="IrExceptionArgumentRole.InnerException"/> role is passed as the
/// ES2022 <c>{ cause }</c> option (the built-in <c>Error</c>) rather than as a
/// positional argument.</param>
public sealed record TsExceptionClass(
    string Name,
    TsTypeOrigin? Origin,
    IReadOnlyList<IrExceptionArgumentRole> ConstructorRoles,
    bool TakesInnerExceptionAsCause = false
)
{
    /// <summary>
    /// The built-in <c>Error</c>, which every exception lowers to when the
    /// compilation maps no runtime class, not even for <c>System.Exception</c>.
    /// </summary>
    public static TsExceptionClass BuiltInError { get; } =
        new(
            "Error",
            Origin: null,
            [IrExceptionArgumentRole.Message, IrExceptionArgumentRole.InnerException],
            TakesInnerExceptionAsCause: true
        );

    /// <summary>The class in type position (annotations, <c>extends</c>).</summary>
    public TsNamedType TypeReference() => new(Name, Origin: Origin);

    /// <summary>The class in value position (<c>new</c>, <c>instanceof</c>), carrying its import.</summary>
    public TsExpression ValueReference() =>
        Origin is null ? new TsIdentifier(Name) : new TsTypeReference(Name, Origin);
}
