using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Converts an <see cref="IrInterfaceDeclaration"/> to a TypeScript
/// <see cref="TsInterface"/> declaration.
/// </summary>
public static class IrToTsInterfaceBridge
{
    public static void Convert(
        IrInterfaceDeclaration ir,
        List<TsTopLevel> statements,
        string? nameOverride = null
    )
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

        // A transformer-provided override (e.g.,
        // `--strip-interface-prefix`) takes precedence over the naming
        // policy so the emitted declaration matches the name the
        // transformer already registered in `TypeNamesBySymbol` (and
        // consequently the file name + import collector).
        var tsName = nameOverride ?? IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var typeParams = ConvertTypeParameters(ir.TypeParameters);
        var extends = BuildExtends(ir.BaseInterfaces);

        statements.Add(
            new TsInterface(
                tsName,
                properties,
                TypeParameters: typeParams,
                Methods: methods.Count > 0 ? methods : null,
                Extends: extends
            )
        );
    }

    /// <summary>
    /// Convert IR base-interface references into TS extends entries.
    /// Delegates to <see cref="IrToTsClassBridge.ConvertHeritageList"/>
    /// so the same filtering and array-shorthand normalization apply
    /// to both <c>extends</c> (interface) and <c>implements</c>
    /// (class) clauses.
    /// </summary>
    public static IReadOnlyList<TsType>? BuildExtends(IReadOnlyList<IrTypeRef>? baseInterfaces) =>
        IrToTsClassBridge.ConvertHeritageList(baseInterfaces);

    private static TsProperty ConvertProperty(IrPropertyDeclaration prop)
    {
        var name = IrToTsNamingPolicy.ToInterfaceMemberName(prop.Name, prop.Attributes);
        var type = IrToTsTypeMapper.Map(prop.Type);
        var isReadonly = prop.Accessors == IrPropertyAccessors.GetOnly;
        return new TsProperty(name, type, isReadonly, Optional: prop.IsOptional);
    }

    private static TsInterfaceMethod ConvertMethod(IrMethodDeclaration method)
    {
        var name = IrToTsNamingPolicy.ToInterfaceMemberName(method.Name, method.Attributes);
        var returnType = IrToTsTypeMapper.Map(method.ReturnType);
        var parameters = method
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.Map(p.Type),
                Optional: p.IsOptional
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
