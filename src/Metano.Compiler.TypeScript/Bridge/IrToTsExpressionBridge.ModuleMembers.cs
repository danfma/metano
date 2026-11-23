using Metano.Compiler.IR;

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
}
