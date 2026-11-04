using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Recognizes the System.Text.Json serialization attributes by their fully
/// qualified metadata name, so a same-named attribute declared in another
/// namespace is ignored.
/// </summary>
internal static class JsonSerializationAttributes
{
    private const string SerializationNamespace = "System.Text.Json.Serialization";

    /// <summary>
    /// Whether <paramref name="attribute"/> is
    /// <c>System.Text.Json.Serialization.<paramref name="metadataName"/></c>.
    /// </summary>
    public static bool Is(AttributeData attribute, string metadataName) =>
        attribute.AttributeClass is { } attributeClass
        && attributeClass.MetadataName == metadataName
        && attributeClass.ContainingNamespace.ToDisplayString() == SerializationNamespace;
}
