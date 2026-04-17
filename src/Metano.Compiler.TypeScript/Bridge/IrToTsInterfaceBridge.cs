using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Converts an <see cref="IrInterfaceDeclaration"/> to a TypeScript
/// <see cref="TsInterface"/> declaration.
/// </summary>
public static class IrToTsInterfaceBridge
{
    public static void Convert(IrInterfaceDeclaration ir, List<TsTopLevel> statements)
    {
        var properties = new List<TsProperty>();
        var methods = new List<TsInterfaceMethod>();

        if (ir.Members is not null)
        {
            foreach (var member in ir.Members)
            {
                switch (member)
                {
                    case IrPropertyDeclaration prop:
                        properties.Add(ConvertProperty(prop));
                        break;

                    case IrMethodDeclaration method:
                        methods.Add(ConvertMethod(method));
                        break;

                    // Events in interfaces are not currently rendered by the legacy
                    // transformer; skip them for output parity.
                }
            }
        }

        var tsName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var typeParams = ConvertTypeParameters(ir.TypeParameters);

        statements.Add(
            new TsInterface(
                tsName,
                properties,
                TypeParameters: typeParams,
                Methods: methods.Count > 0 ? methods : null
            )
        );
    }

    private static TsProperty ConvertProperty(IrPropertyDeclaration prop)
    {
        var name = IrToTsNamingPolicy.ToInterfaceMemberName(prop.Name, prop.Attributes);
        var type = IrToTsTypeMapper.Map(prop.Type);
        var isReadonly = prop.Accessors == IrPropertyAccessors.GetOnly;
        return new TsProperty(name, type, isReadonly);
    }

    private static TsInterfaceMethod ConvertMethod(IrMethodDeclaration method)
    {
        var name = IrToTsNamingPolicy.ToInterfaceMemberName(method.Name, method.Attributes);
        var returnType = IrToTsTypeMapper.Map(method.ReturnType);
        var parameters = method
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.Map(p.Type)
            ))
            .ToList();
        var typeParams = ConvertTypeParameters(method.TypeParameters);
        return new TsInterfaceMethod(name, parameters, returnType, typeParams);
    }

    /// <summary>
    /// Converts IR type parameters to TS. Matches legacy behavior of keeping only the
    /// first constraint (<c>where T : IFoo, IBar</c> → <c>T extends IFoo</c>) — the
    /// TypeScript target can only express a single constraint as an <c>extends</c> clause
    /// without resorting to intersection types.
    /// </summary>
    private static IReadOnlyList<TsTypeParameter>? ConvertTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;

        return typeParameters
            .Select(tp =>
            {
                TsType? constraint = tp.Constraints is { Count: > 0 } c
                    ? IrToTsTypeMapper.Map(c[0])
                    : null;
                return new TsTypeParameter(tp.Name, constraint);
            })
            .ToList();
    }
}
