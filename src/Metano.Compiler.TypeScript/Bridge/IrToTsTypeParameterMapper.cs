using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

internal static class IrToTsTypeParameterMapper
{
    /// <summary>
    /// The type parameters a TypeScript declaration of <paramref name="declaration"/>
    /// declares: those of its generic containers, which a declaration lowered
    /// outside its container must bind itself (#436), then its own.
    /// </summary>
    public static IReadOnlyList<TsTypeParameter>? ConvertDeclared(IrTypeDeclaration declaration) =>
        Convert(
            declaration.ContainerTypeParameters is { } container
                ? [.. container, .. declaration.TypeParameters ?? []]
                : declaration.TypeParameters
        );

    public static IReadOnlyList<TsTypeParameter>? Convert(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;

        return typeParameters
            .Select(tp =>
            {
                TsType? constraint = tp.Constraints switch
                {
                    null or { Count: 0 } => null,
                    { Count: 1 } single => IrToTsTypeMapper.Map(single[0]),
                    // Multi-constraint `where T : A, B, C` lowers to the
                    // TS intersection form `T extends A & B & C` so every
                    // bound is preserved on the emit side. (#153)
                    { } many => new TsIntersectionType(
                        many.Select(t => IrToTsTypeMapper.Map(t)).ToList()
                    ),
                };
                return new TsTypeParameter(tp.Name, constraint);
            })
            .ToList();
    }
}
