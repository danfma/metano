using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

public static class IrToTsDelegateBridge
{
    public static void Convert(
        IrDelegateDeclaration ir,
        List<TsTopLevel> statements,
        string? nameOverride = null
    )
    {
        var name = nameOverride ?? IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var parameters = ir
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.Map(p.Type),
                Optional: p.IsOptional,
                Rest: p.IsParams
            ))
            .ToList();
        var returnType = IrToTsTypeMapper.Map(ir.ReturnType);
        var thisType = ir.ThisType is null ? null : IrToTsTypeMapper.Map(ir.ThisType);
        var typeParameters = IrToTsTypeParameterMapper.Convert(ir.TypeParameters);

        var functionType = new TsFunctionType(parameters, returnType, thisType);

        statements.Add(new TsTypeAlias(name, functionType, Exported: true, typeParameters));
    }
}
