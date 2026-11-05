using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The FR-026 rule of spec 013 (contracts/naming.md § FR-026): whether a
/// derived class's factory may keep the name of a factory its static side
/// inherits, emitted <c>static override</c>, or must be qualified with the
/// type name. TypeScript checks the static side of <c>extends</c> (TS2417), so
/// the kept factory must be assignable to the inherited one; the rule is a
/// conservative, declaration-only stand-in for that check.
/// </summary>
internal static class TsFactoryInheritance
{
    /// <summary>
    /// The static side a class inherits from its base, each factory rewritten
    /// into the class's own type parameters through its <c>extends</c> clause:
    /// a base type parameter bound to one of the class's type parameters is
    /// renamed to it (<c>Box2&lt;U&gt; : Box&lt;U&gt;</c>); a factory whose
    /// type parameter the clause fixes (<c>IntBox : Box&lt;int&gt;</c>) or
    /// binds twice can never be redeclared.
    /// </summary>
    /// <param name="baseStatics">The base's whole static side.</param>
    /// <param name="extendsType">The class's <c>extends</c> clause.</param>
    /// <param name="typeParameterNames">The class's own type parameters.</param>
    public static IReadOnlyDictionary<string, TsClaimedMember> Inherit(
        IReadOnlyDictionary<string, TsClaimedMember> baseStatics,
        TsType extendsType,
        IReadOnlyList<string> typeParameterNames
    )
    {
        var baseArguments = extendsType is TsNamedType { TypeArguments: { } arguments }
            ? arguments
            : [];

        return baseStatics.ToDictionary(
            entry => entry.Key,
            entry =>
                entry.Value.Factory is { } factory
                    ? entry.Value with
                    {
                        Factory = Rebind(factory, baseArguments, typeParameterNames),
                    }
                    : entry.Value,
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Whether <paramref name="derived"/> may redeclare
    /// <paramref name="inherited"/> under its name: the inherited factory is
    /// not <c>private</c> and binds no type parameter, the derived one is at
    /// least as accessible, the parameter lists match (types, optional and
    /// rest shape, in order), and each inherited type parameter is one of the
    /// derived factory's with the same constraint.
    /// </summary>
    public static bool CanRedeclare(TsFactorySignature inherited, TsFactorySignature derived) =>
        inherited.Accessibility is not TsAccessibility.Private
        && !inherited.BindsTypeParameters
        && !derived.Accessibility.IsNarrowerThan(inherited.Accessibility)
        && HaveSameParameters(inherited.Parameters, derived.Parameters)
        && KeepsTypeParameters(inherited.TypeParameters, derived.TypeParameters);

    private static TsFactorySignature Rebind(
        TsFactorySignature factory,
        IReadOnlyList<TsType> baseArguments,
        IReadOnlyList<string> typeParameterNames
    )
    {
        if (factory.TypeParameters.Count == 0)
            return factory;

        if (BindingOf(factory.TypeParameters, baseArguments, typeParameterNames) is not { } binding)
            return factory with { BindsTypeParameters = true };

        return factory with
        {
            TypeParameters = factory
                .TypeParameters.Select(parameter => new TsTypeParameter(
                    binding[parameter.Name],
                    parameter.Constraint is { } constraint
                        ? TsTypeParameterRenaming.Rename(constraint, binding)
                        : null
                ))
                .ToList(),
            Parameters = factory
                .Parameters.Select(parameter =>
                    parameter with
                    {
                        Type = TsTypeParameterRenaming.Rename(parameter.Type, binding),
                    }
                )
                .ToList(),
        };
    }

    /// <summary>
    /// Each base type parameter → the class type parameter its
    /// <c>extends</c> argument names; <c>null</c> when an argument is anything
    /// else or two arguments name the same type parameter.
    /// </summary>
    private static Dictionary<string, string>? BindingOf(
        IReadOnlyList<TsTypeParameter> baseTypeParameters,
        IReadOnlyList<TsType> baseArguments,
        IReadOnlyList<string> typeParameterNames
    )
    {
        if (baseArguments.Count != baseTypeParameters.Count)
            return null;

        var binding = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < baseTypeParameters.Count; index++)
        {
            if (TypeParameterNamedBy(baseArguments[index], typeParameterNames) is not { } bound)
                return null;

            if (binding.ContainsValue(bound))
                return null;

            binding[baseTypeParameters[index].Name] = bound;
        }

        return binding;
    }

    private static string? TypeParameterNamedBy(
        TsType argument,
        IReadOnlyList<string> typeParameterNames
    ) =>
        argument is TsNamedType { TypeArguments: null or { Count: 0 } } named
        && typeParameterNames.Contains(named.Name)
            ? named.Name
            : null;

    private static bool HaveSameParameters(
        IReadOnlyList<TsFactoryParameter> inherited,
        IReadOnlyList<TsFactoryParameter> derived
    ) =>
        inherited.Count == derived.Count
        && inherited
            .Zip(derived)
            .All(pair =>
                pair.First.CanBeOmitted == pair.Second.CanBeOmitted
                && pair.First.Rest == pair.Second.Rest
                && Printer.RenderType(pair.First.Type) == Printer.RenderType(pair.Second.Type)
            );

    private static bool KeepsTypeParameters(
        IReadOnlyList<TsTypeParameter> inherited,
        IReadOnlyList<TsTypeParameter> derived
    ) =>
        inherited.All(parameter =>
            derived.FirstOrDefault(candidate => candidate.Name == parameter.Name) is { } kept
            && ConstraintText(kept) == ConstraintText(parameter)
        );

    private static string? ConstraintText(TsTypeParameter parameter) =>
        parameter.Constraint is { } constraint ? Printer.RenderType(constraint) : null;
}
