using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The runtime module generated contexts import their base class and spec
/// types from: <c>metano-runtime/system/json</c>.
/// </summary>
internal static class JsonRuntimeModule
{
    public static readonly TsTypeOrigin Origin = new("metano-runtime", "system/json");
}
