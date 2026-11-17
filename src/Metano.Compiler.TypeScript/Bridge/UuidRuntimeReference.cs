using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The <c>metano-runtime</c> <c>UUID</c> namespace C#'s <c>Guid</c> lowers to,
/// referenced by value (<c>UUID.empty</c>, <c>UUID.parse</c>): a runtime-helper
/// reference, so the import collector imports it.
/// </summary>
internal static class UuidRuntimeReference
{
    public const string Name = "UUID";

    public static TsRuntimeHelperReference Value() => new(Name);
}
