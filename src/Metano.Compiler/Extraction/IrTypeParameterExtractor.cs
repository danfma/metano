using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts the type parameter declarations of a generic type, method or
/// delegate into <see cref="IrTypeParameter"/>s.
/// </summary>
public static class IrTypeParameterExtractor
{
    /// <summary>
    /// The declarations of <paramref name="typeParameters"/>, or <c>null</c>
    /// when there are none.
    /// </summary>
    public static IReadOnlyList<IrTypeParameter>? ExtractAll(
        IReadOnlyList<ITypeParameterSymbol> typeParameters,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target = null
    ) =>
        typeParameters.Count > 0
            ? typeParameters.Select(tp => Extract(tp, originResolver, target)).ToList()
            : null;

    public static IrTypeParameter Extract(
        ITypeParameterSymbol typeParameter,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target = null
    ) =>
        new(
            typeParameter.Name,
            typeParameter.ConstraintTypes.Length > 0
                ? typeParameter
                    .ConstraintTypes.Select(t => IrTypeRefMapper.Map(t, originResolver, target))
                    .ToList()
                : null,
            KindConstraintOf(typeParameter)
        );

    private static IrTypeKindConstraint KindConstraintOf(ITypeParameterSymbol typeParameter)
    {
        if (typeParameter.HasReferenceTypeConstraint)
            return IrTypeKindConstraint.ReferenceType;

        return typeParameter.HasValueTypeConstraint || typeParameter.HasUnmanagedTypeConstraint
            ? IrTypeKindConstraint.ValueType
            : IrTypeKindConstraint.Any;
    }
}
