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
        IrExtractionContext? context = null
    )
    {
        var originResolver = context?.OriginResolver;
        var target = context?.Target;
        var members = new List<IrMemberDeclaration>();
        var groupSuffixes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

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
                    members.Add(ExtractProperty(prop, originResolver, target));
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    members.Add(
                        NamedInGroup(
                            type,
                            method,
                            target,
                            ExtractMethod(method, originResolver, target),
                            groupSuffixes
                        )
                    );
                    break;

                case IEventSymbol evt:
                    members.Add(ExtractEvent(evt, originResolver, target));
                    break;
            }
        }

        var visibility = IrVisibilityMapper.Map(type.DeclaredAccessibility);

        var emittedBaseInterfaces = SymbolHelper.EmittedInterfaces(type, target);

        var baseInterfaces =
            emittedBaseInterfaces.Count > 0
                ? emittedBaseInterfaces
                    .Select(i => IrTypeRefMapper.Map(i, originResolver, target))
                    .ToList()
                : null;

        var typeParameters =
            type.TypeParameters.Length > 0
                ? type
                    .TypeParameters.Select(tp => ExtractTypeParameter(tp, originResolver, target))
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

    /// <summary>
    /// A method of an overload group of the interface (spec 013 FR-031) is
    /// named over its group, which every implementer anchors on: its suffix,
    /// its signature text and its declaration ordinal. Any other method is
    /// returned unchanged.
    /// </summary>
    private static IrMethodDeclaration NamedInGroup(
        INamedTypeSymbol type,
        IMethodSymbol method,
        Metano.Annotations.TargetLanguage? target,
        IrMethodDeclaration declaration,
        Dictionary<string, IReadOnlyList<string>> groupSuffixes
    )
    {
        var group = MethodOverloadGroups.InterfaceGroupOf(type, method.Name, target);
        var ordinal = group.IndexOf(method);

        if (group.Count < 2 || ordinal < 0)
            return declaration;

        if (!groupSuffixes.TryGetValue(method.Name, out var suffixes))
        {
            suffixes = OverloadGroupNaming.SuffixesOf(group);
            groupSuffixes[method.Name] = suffixes;
        }

        var named = OverloadGroupNaming.Apply([(method, declaration)], _ => suffixes[ordinal])[
            method
        ];

        return named with
        {
            DeclarationOrdinal = ordinal,
        };
    }

    private static IrPropertyDeclaration ExtractProperty(
        IPropertySymbol prop,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
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
            IrTypeRefMapper.Map(prop.Type, originResolver, target),
            accessors,
            IsOptional: prop.HasOptional()
        )
        {
            Attributes = IrAttributeExtractor.Extract(prop),
        };
    }

    private static IrMethodDeclaration ExtractMethod(
        IMethodSymbol method,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var parameters = method
            .Parameters.Select(p =>
                OutParameterFlow.Annotate(
                    new IrParameter(
                        p.Name,
                        IrTypeRefMapper.Map(p.Type, originResolver, target),
                        IsParams: p.IsParams,
                        IsOptional: p.HasOptional(),
                        IsConstant: p.HasConstant()
                    ),
                    p,
                    originResolver,
                    target
                )
            )
            .ToList();

        var returnType = IrTypeRefMapper.Map(method.ReturnType, originResolver, target);

        var typeParams =
            method.TypeParameters.Length > 0
                ? method
                    .TypeParameters.Select(tp => ExtractTypeParameter(tp, originResolver, target))
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
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    ) =>
        new(
            evt.Name,
            IrVisibility.Public,
            evt.IsStatic,
            IrTypeRefMapper.Map(evt.Type, originResolver, target)
        )
        {
            Attributes = IrAttributeExtractor.Extract(evt),
        };

    private static IrTypeParameter ExtractTypeParameter(
        ITypeParameterSymbol tp,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    ) =>
        new(
            tp.Name,
            tp.ConstraintTypes.Length > 0
                ? tp
                    .ConstraintTypes.Select(t => IrTypeRefMapper.Map(t, originResolver, target))
                    .ToList()
                : null
        );
}
