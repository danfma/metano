namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>
/// The reachability result of the running transform, published for the
/// duration of the group transforms (as <c>IrToTsTypeMapper</c> publishes its
/// scopes), so every bridge that renders a descriptor read checks it against
/// the reached set. <c>null</c> outside a transform (a bridge driven by a unit
/// test directly), where no assertion runs.
/// </summary>
internal static class RuntimeTypeScope
{
    private static readonly AsyncLocal<RuntimeTypeReachabilityResult?> Result = new();

    public static RuntimeTypeReachabilityResult? Current
    {
        get => Result.Value;
        set => Result.Value = value;
    }
}
