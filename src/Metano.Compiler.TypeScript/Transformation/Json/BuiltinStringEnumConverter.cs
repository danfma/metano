using Metano.Compiler;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Tells whether a <c>[JsonConverter]</c> is System.Text.Json's own
/// <c>JsonStringEnumConverter</c> (or <c>JsonStringEnumConverter&lt;TEnum&gt;</c>)
/// over a <c>[StringEnum]</c>: the attribute cannot pass a naming policy or
/// <c>allowIntegerValues</c>, so the converter is always the default one,
/// which writes the member names the string-enum descriptor writes. The
/// descriptor reads only the exact names, while the converter also reads a
/// name in another case, an integer and a comma-separated list: a payload
/// the descriptor writes always reads on both sides, so nothing is reported.
/// A numeric enum, whose descriptor writes numbers, and any other converter
/// type, including a subclass, are not matched.
/// </summary>
internal static class BuiltinStringEnumConverter
{
    private const string ConverterName = "JsonStringEnumConverter";

    private const string ConverterNamespace = "System.Text.Json.Serialization";

    /// <summary>True when <paramref name="attribute"/> is the built-in converter over <paramref name="target"/>, a <c>[StringEnum]</c>.</summary>
    public static bool Matches(AttributeData attribute, ITypeSymbol target)
    {
        if (UnwrapNullable(target) is not { TypeKind: TypeKind.Enum } enumType)
            return false;

        if (!SymbolHelper.HasStringEnum(enumType))
            return false;

        return attribute.ConstructorArguments is [{ Value: INamedTypeSymbol converter }]
            && IsBuiltinConverterOver(converter, enumType);
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

    private static bool IsBuiltinConverterOver(INamedTypeSymbol converter, ITypeSymbol enumType)
    {
        if (converter.ContainingNamespace.ToDisplayString() != ConverterNamespace)
            return false;

        if (converter.Name != ConverterName)
            return false;

        return converter.TypeArguments.Length switch
        {
            0 => true,
            1 => SymbolEqualityComparer.Default.Equals(converter.TypeArguments[0], enumType),
            _ => false,
        };
    }
}
