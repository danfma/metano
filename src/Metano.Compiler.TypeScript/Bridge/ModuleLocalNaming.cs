using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript naming policy of a <c>private</c> nested type (#412). A merged
/// namespace member cannot be hidden from the owner class, so the type is declared
/// module-local and unexported under one identifier — the path of its containers up to
/// itself joined by <see cref="Separator"/> (<c>Outer$Inner</c>). The core IR keeps the
/// dotted path and only records how many leading segments are module-local; this class
/// spells them for declarations (<see cref="DeclaredName"/>, <see cref="Apply"/>) and
/// references (<see cref="Flatten"/>) alike, so both always agree.
/// </summary>
internal static class ModuleLocalNaming
{
    /// <summary>
    /// <c>$</c> is a legal TypeScript identifier character that no C# type name contains.
    /// </summary>
    public const string Separator = "$";

    /// <summary>
    /// The module-local identifier <paramref name="type"/> is declared under, or
    /// <c>null</c> when it is not a <c>private</c> nested type.
    /// </summary>
    public static string? DeclaredName(INamedTypeSymbol type)
    {
        if (!SymbolHelper.IsContainerPrivateNestedType(type))
            return null;

        var segments = new List<string>();

        for (var current = type; current is not null; current = current.ContainingType)
            segments.Insert(
                0,
                SymbolHelper.GetNameOverride(current, TargetLanguage.TypeScript) ?? current.Name
            );

        return string.Join(Separator, segments);
    }

    /// <summary>
    /// Spells a dotted IR reference name for TypeScript: its first
    /// <paramref name="moduleLocalSegments"/> segments collapse into the module-local
    /// identifier, the rest stay dotted (<c>Outer$Inner.Leaf</c>).
    /// </summary>
    public static string Flatten(string dottedName, int moduleLocalSegments)
    {
        if (moduleLocalSegments < 2)
            return dottedName;

        var segments = dottedName.Split('.');

        if (segments.Length < moduleLocalSegments)
            return dottedName;

        var collapsed = string.Join(Separator, segments.Take(moduleLocalSegments));

        return string.Join(".", segments.Skip(moduleLocalSegments).Prepend(collapsed));
    }

    /// <summary>
    /// Renames the IR declaration of a <c>private</c> nested type to its module-local
    /// identifier. The bridges read the declared name from
    /// <see cref="IrTypeDeclaration.Name"/> plus its <c>[Name]</c> override, so the
    /// rename is applied once on the IR before any bridge sees it.
    /// </summary>
    public static IrTypeDeclaration Apply(IrTypeDeclaration declaration, INamedTypeSymbol type)
    {
        if (DeclaredName(type) is not { } name)
            return declaration;

        var attributes = (declaration.Attributes ?? [])
            .Where(attribute => attribute.Name != IrAttribute.NameOverrideAttribute)
            .Append(IrAttribute.NameOverride(name))
            .ToList();

        return declaration switch
        {
            IrClassDeclaration cls => cls with { Name = name, Attributes = attributes },
            IrInterfaceDeclaration iface => iface with { Name = name, Attributes = attributes },
            IrEnumDeclaration enumeration => enumeration with
            {
                Name = name,
                Attributes = attributes,
            },
            IrDelegateDeclaration del => del with { Name = name, Attributes = attributes },
            _ => declaration,
        };
    }
}
