using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// Whether a static member access drops its type qualifier because the type
    /// emits no class: a member of a containerless class, or a static method of an
    /// extension container, which TypeScript emits as a module of functions (#554).
    /// </summary>
    private static bool FlattensToModuleMember(IrMemberOrigin origin) =>
        origin is { IsStatic: true, IsDeclaringTypeNoContainer: true }
        || origin.IsDeclaringTypeExtensionContainer;

    /// <summary>
    /// The flattened reference to a member of a module-shaped class. A field or
    /// property is a module variable of the container's file (#314), and a static
    /// method of an extension container is a function of it (#554): both carry the
    /// owner, so another file imports the name from that module rather than from any
    /// same-named function of the package. A containerless function keeps its bare
    /// name, which the package-wide function registry resolves.
    /// </summary>
    private static TsExpression ModuleMemberReference(
        IrMemberOrigin origin,
        string memberName,
        IrTypeReference container
    ) =>
        origin.IsTranspiledDataMember || origin.IsDeclaringTypeExtensionContainer
            ? ModuleExportReference(memberName, container)
            : new TsIdentifier(memberName);
}
