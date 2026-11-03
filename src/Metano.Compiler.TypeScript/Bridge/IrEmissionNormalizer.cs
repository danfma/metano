using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The single normalization every extracted type declaration goes through before a
/// bridge sees it: <c>out</c> parameter lowering, then the module-local rename of a
/// <c>private</c> nested type.
/// </summary>
internal static class IrEmissionNormalizer
{
    public static IrTypeDeclaration Normalize(
        IrTypeDeclaration declaration,
        INamedTypeSymbol type
    ) => ModuleLocalNaming.Apply(OutParameterLowering.Lower(declaration), type);

    public static IrClassDeclaration Normalize(
        IrClassDeclaration declaration,
        INamedTypeSymbol type
    ) => (IrClassDeclaration)Normalize((IrTypeDeclaration)declaration, type);
}
