using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts an <see cref="IrInterfaceDeclaration"/> from a Roslyn <see cref="INamedTypeSymbol"/>
/// representing a C# interface. Purely semantic — names stay in original C# casing.
/// </summary>
public static class IrInterfaceExtractor
{
    public static IrInterfaceDeclaration Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var members = new List<IrMemberDeclaration>();

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;
            if (member.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (SymbolHelper.HasIgnore(member, target))
                continue;

            switch (member)
            {
                case IPropertySymbol prop:
                    members.Add(ExtractProperty(prop, originResolver));
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    members.Add(ExtractMethod(method, originResolver));
                    break;

                case IEventSymbol evt:
                    members.Add(ExtractEvent(evt, originResolver));
                    break;
            }
        }

        var visibility = IrVisibilityMapper.Map(type.DeclaredAccessibility);

        var baseInterfaces =
            type.Interfaces.Length > 0
                ? type.Interfaces.Select(i => IrTypeRefMapper.Map(i, originResolver)).ToList()
                : null;

        var typeParameters =
            type.TypeParameters.Length > 0
                ? type
                    .TypeParameters.Select(tp => ExtractTypeParameter(tp, originResolver))
                    .ToList()
                : null;

        return new IrInterfaceDeclaration(
            type.Name,
            visibility,
            baseInterfaces,
            members.Count > 0 ? members : null,
            typeParameters,
            Attributes: IrAttributeExtractor.Extract(type)
        );
    }

    private static IrPropertyDeclaration ExtractProperty(
        IPropertySymbol prop,
        IrTypeOriginResolver? originResolver
    )
    {
        var accessors =
            prop.SetMethod is null || prop.SetMethod.IsInitOnly
                ? IrPropertyAccessors.GetOnly
                : IrPropertyAccessors.GetSet;

        return new IrPropertyDeclaration(
            prop.Name,
            IrVisibility.Public,
            prop.IsStatic,
            IrTypeRefMapper.Map(prop.Type, originResolver),
            accessors
        )
        {
            Attributes = IrAttributeExtractor.Extract(prop),
        };
    }

    private static IrMethodDeclaration ExtractMethod(
        IMethodSymbol method,
        IrTypeOriginResolver? originResolver
    )
    {
        var parameters = method
            .Parameters.Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, originResolver)
            ))
            .ToList();

        var returnType = IrTypeRefMapper.Map(method.ReturnType, originResolver);

        var typeParams =
            method.TypeParameters.Length > 0
                ? method
                    .TypeParameters.Select(tp => ExtractTypeParameter(tp, originResolver))
                    .ToList()
                : null;

        // In C# 8+ an interface method may declare a default implementation.
        // Such members expose a non-null Body in the syntax tree and Roslyn reports
        // IsAbstract == false. We record the semantic signal here; the actual body
        // extraction is a later-phase concern.
        var hasDefaultImpl =
            method.ContainingType.TypeKind == TypeKind.Interface && !method.IsAbstract;

        return new IrMethodDeclaration(
            method.Name,
            IrVisibility.Public,
            method.IsStatic,
            parameters,
            returnType,
            Body: null,
            new IrMethodSemantics(
                IsAsync: method.IsAsync,
                IsExtension: method.IsExtensionMethod,
                HasDefaultImplementation: hasDefaultImpl
            ),
            TypeParameters: typeParams
        )
        {
            Attributes = IrAttributeExtractor.Extract(method),
        };
    }

    private static IrEventDeclaration ExtractEvent(
        IEventSymbol evt,
        IrTypeOriginResolver? originResolver
    ) =>
        new(
            evt.Name,
            IrVisibility.Public,
            evt.IsStatic,
            IrTypeRefMapper.Map(evt.Type, originResolver)
        )
        {
            Attributes = IrAttributeExtractor.Extract(evt),
        };

    private static IrTypeParameter ExtractTypeParameter(
        ITypeParameterSymbol tp,
        IrTypeOriginResolver? originResolver
    ) =>
        new(
            tp.Name,
            tp.ConstraintTypes.Length > 0
                ? tp.ConstraintTypes.Select(t => IrTypeRefMapper.Map(t, originResolver)).ToList()
                : null
        );
}
