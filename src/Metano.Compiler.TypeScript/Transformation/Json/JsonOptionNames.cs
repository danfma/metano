namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The named arguments of <c>[JsonSourceGenerationOptions]</c> that more than one
/// reader of the attribute looks up, spelled once.
/// </summary>
internal static class JsonOptionNames
{
    public const string NumberHandling = "NumberHandling";

    public const string DefaultIgnoreCondition = "DefaultIgnoreCondition";

    public const string WriteIndented = "WriteIndented";

    public const string IndentCharacter = "IndentCharacter";

    public const string IndentSize = "IndentSize";

    public const string NewLine = "NewLine";
}
