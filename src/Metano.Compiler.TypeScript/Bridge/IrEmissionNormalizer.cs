using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The single normalization every extracted type declaration goes through before a
/// bridge sees it: <c>out</c> parameter lowering, then the rename of a nested type
/// declared at module level to its module-local identifier
/// (<see cref="ModuleLocalNaming.Apply"/>).
/// </summary>
internal static class IrEmissionNormalizer
{
    public static IrTypeDeclaration Normalize(
        IrTypeDeclaration declaration,
        string? moduleLevelName
    ) => ModuleLocalNaming.Apply(OutParameterLowering.Lower(declaration), moduleLevelName);

    public static IrClassDeclaration Normalize(
        IrClassDeclaration declaration,
        string? moduleLevelName
    ) => (IrClassDeclaration)Normalize((IrTypeDeclaration)declaration, moduleLevelName);
}
