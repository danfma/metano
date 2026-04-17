using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts an <see cref="IrEnumDeclaration"/> from a Roslyn <see cref="INamedTypeSymbol"/>
/// representing a C# enum. This is purely semantic — no target-specific decisions are made.
/// </summary>
public static class IrEnumExtractor
{
    public static IrEnumDeclaration Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null
    )
    {
        _ = originResolver; // currently unused for enums; reserved for future consistency
        var isStringEnum = SymbolHelper.HasStringEnum(type);
        var style = isStringEnum ? IrEnumStyle.String : IrEnumStyle.Numeric;

        var members = new List<IrEnumMember>();

        foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.HasConstantValue)
                continue;

            var nameOverride = SymbolHelper.GetNameOverride(member);
            var attributes = IrAttributeExtractor.Extract(member);

            // For string enums, the "value" is the name override or the member name.
            // For numeric enums, the value is the constant numeric value.
            object? value = isStringEnum ? (nameOverride ?? member.Name) : member.ConstantValue;

            members.Add(new IrEnumMember(member.Name, value, attributes));
        }

        var visibility = IrVisibilityMapper.Map(type.DeclaredAccessibility);

        return new IrEnumDeclaration(
            type.Name,
            visibility,
            members,
            style,
            Attributes: IrAttributeExtractor.Extract(type)
        );
    }
}
