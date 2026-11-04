using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

public static class IrDelegateExtractor
{
    public static IrDelegateDeclaration? Extract(
        INamedTypeSymbol type,
        IrExtractionContext? context = null
    )
    {
        var originResolver = context?.OriginResolver;
        var target = context?.Target;

        if (type.TypeKind != TypeKind.Delegate)
            return null;

        var invoke = type.GetMembers("Invoke").OfType<IMethodSymbol>().FirstOrDefault();
        if (invoke is null)
            return null;

        IrTypeRef? thisType = null;
        var sourceParameters = invoke.Parameters;
        if (sourceParameters.Length > 0 && SymbolHelper.HasThis(sourceParameters[0]))
        {
            thisType = IrTypeRefMapper.Map(sourceParameters[0].Type, originResolver, target);
            sourceParameters = sourceParameters.RemoveAt(0);
        }

        var parameters = sourceParameters
            .Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, originResolver, target),
                HasDefaultValue: p.HasExplicitDefaultValue,
                IsParams: p.IsParams,
                IsOptional: p.HasOptional()
            ))
            .ToList();

        var returnType = invoke.ReturnsVoid
            ? new IrPrimitiveTypeRef(IrPrimitive.Void)
            : IrTypeRefMapper.Map(invoke.ReturnType, originResolver, target);

        var typeParameters = IrTypeParameterExtractor.ExtractAll(
            type.TypeParameters,
            originResolver,
            target
        );

        return new IrDelegateDeclaration(
            Name: type.Name,
            Visibility: IrVisibilityMapper.Map(type.DeclaredAccessibility),
            Parameters: parameters,
            ReturnType: returnType,
            ThisType: thisType,
            TypeParameters: typeParameters,
            Attributes: IrAttributeExtractor.Extract(type)
        );
    }
}
