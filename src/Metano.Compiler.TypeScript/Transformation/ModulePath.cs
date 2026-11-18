namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The module path of a generated file: its path relative to the output root,
/// without extension and with <c>/</c> separators — the key the barrel orders
/// its export lines by and the barrel hoists name modules with.
/// </summary>
internal static class ModulePath
{
    /// <summary>The module path of the file at <paramref name="relativePath"/>.</summary>
    public static string Of(string relativePath) =>
        Path.ChangeExtension(relativePath, null).Replace('\\', '/');
}
