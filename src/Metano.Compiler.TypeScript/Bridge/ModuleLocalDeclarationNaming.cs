using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renames the IR declaration of a <c>private</c> nested type to the module-local
/// identifier every reference to it spells (<c>Outer$Inner</c>, #412). The bridges
/// read the declared name from <see cref="IrTypeDeclaration.Name"/> plus its
/// <c>[Name]</c> override, so the rename is applied once on the IR before any bridge
/// sees it and every declaration site (class, enum, interface, record synthesis,
/// plain object…) agrees with the references.
/// </summary>
internal static class ModuleLocalDeclarationNaming
{
    public static IrClassDeclaration Apply(IrClassDeclaration declaration, INamedTypeSymbol type) =>
        (IrClassDeclaration)Apply((IrTypeDeclaration)declaration, type);

    public static IrTypeDeclaration Apply(IrTypeDeclaration declaration, INamedTypeSymbol type)
    {
        var name = IrTypeRefMapper.GetModuleLocalName(type, TargetLanguage.TypeScript);

        if (name is null)
            return declaration;

        var attributes = (declaration.Attributes ?? [])
            .Where(attribute => attribute.Name != "Name")
            .Append(new IrAttribute("Name", new Dictionary<string, object?> { ["Value"] = name }))
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
