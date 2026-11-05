using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The static members spec 013 gives every overload of a dispatched
/// constructor (FR-007, FR-008, FR-011, FR-028):
/// <list type="bullet">
///   <item>a selector, <c>protected static readonly byMoneyAmount: unique symbol
///   = Symbol("Money(decimal amount)")</c>, which the constructor's guarded
///   selector branch recognizes (<see cref="IrToTsConstructorDispatcherBridge"/>);</item>
///   <item>for a concrete class, a factory with the overload's own
///   accessibility, <c>static fromAmount(amount: Decimal): Money { return new
///   Money(Money.byMoneyAmount, amount); }</c>, which re-declares a generic
///   class's type parameters with their constraints and is
///   <c>static override</c> when it keeps an inherited factory's name
///   (FR-026).</item>
/// </list>
/// The names come from the class's claim pass (<see cref="TsOverloadClaims"/>).
/// </summary>
internal static class IrToTsConstructorFactoryBridge
{
    private const string SymbolConstructor = "Symbol";

    /// <summary>
    /// One selector per overload, in declaration order: the class's first
    /// static members (FR-029), so a static initializer that constructs the
    /// class finds them initialized.
    /// </summary>
    public static IReadOnlyList<TsFieldMember> BuildSelectors(
        IrConstructorDeclaration constructor,
        TsOverloadClaims claims
    ) =>
        constructor
            .DeclarationsInOrder.Select(overload => new TsFieldMember(
                claims.NamesOf(overload).Selector,
                new TsUniqueSymbolType(),
                new TsCallExpression(
                    new TsIdentifier(SymbolConstructor),
                    [new TsStringLiteral(DispatchedOverload.SignatureOf(overload))]
                ),
                Readonly: true,
                Static: true,
                Accessibility: TsAccessibility.Protected
            ))
            .ToList();

    /// <summary>
    /// One factory per overload, in declaration order; none for an abstract
    /// class (FR-012).
    /// </summary>
    /// <param name="type">The class.</param>
    /// <param name="typeName">The class's emitted name.</param>
    public static IReadOnlyList<TsMethodMember> BuildFactories(
        IrClassDeclaration type,
        string typeName,
        TsOverloadClaims claims,
        IrToTsBridgeContext context
    )
    {
        if (type.Semantics.IsAbstract || type.Constructor is not { } constructor)
            return [];

        return constructor
            .DeclarationsInOrder.Select(overload =>
                BuildFactory(type, new ConstructorSelectors(typeName, claims), overload, context)
            )
            .ToList();
    }

    /// <summary>
    /// The factory of <paramref name="constructor"/> as an inheriting class's
    /// claim pass compares it (FR-026): its parameter types, optional and rest
    /// shape, accessibility and re-declared type parameters.
    /// </summary>
    public static TsFactorySignature SignatureOf(
        IrClassDeclaration type,
        IrConstructorDeclaration constructor,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new(
            DispatchedOverload.SignatureOf(constructor),
            IrToTsTypeParameterMapper.Convert(type.TypeParameters) ?? [],
            constructor.Parameters.Select(parameter => ShapeOf(parameter, typeOverrides)).ToList(),
            VisibilityOf(constructor).Accessibility
        );

    private static TsMethodMember BuildFactory(
        IrClassDeclaration type,
        ConstructorSelectors selectors,
        IrConstructorDeclaration overload,
        IrToTsBridgeContext context
    )
    {
        var names = selectors.Claims.NamesOf(overload);
        var typeParameters = IrToTsTypeParameterMapper.Convert(type.TypeParameters);
        var parameters = FactoryParameters(overload, context);
        var (accessibility, jsDoc) = VisibilityOf(overload);

        var construction = new TsNewExpression(
            new TsIdentifier(selectors.TypeName),
            [selectors.ReferenceTo(overload), .. parameters.Select(ForwardedArgument)]
        );

        var factoryName =
            names.Factory
            ?? throw new InvalidOperationException(
                $"The claim pass named no factory for '{DispatchedOverload.SignatureOf(overload)}'."
            );

        return new TsMethodMember(
            factoryName,
            parameters,
            SelfType(selectors.TypeName, typeParameters),
            [new TsReturnStatement(construction)],
            Static: true,
            Accessibility: accessibility,
            TypeParameters: typeParameters,
            Override: names.OverridesInheritedFactory,
            JSDoc: jsDoc
        );
    }

    /// <summary>
    /// The overload's parameters as the factory declares them: local-binding
    /// names, the C# defaults, and the shape <see cref="SignatureOf"/>
    /// compares (<c>params</c> as a rest parameter).
    /// </summary>
    private static List<TsParameter> FactoryParameters(
        IrConstructorDeclaration overload,
        IrToTsBridgeContext context
    ) =>
        overload
            .Parameters.Select(parameter =>
            {
                var shape = ShapeOf(parameter, context.TypeOverrides);

                return new TsParameter(
                    IrToTsNamingPolicy.ToParameterName(parameter.Parameter.Name),
                    shape.Type,
                    Optional: shape.CanBeOmitted,
                    DefaultValue: IrToTsClassBridge.ResolveCtorParamDefault(parameter, context),
                    Rest: shape.Rest
                );
            })
            .ToList();

    /// <summary>
    /// One parameter's type and its optional and rest shape: the single
    /// mapping both the emitted factory and its FR-026 comparison read.
    /// </summary>
    private static TsFactoryParameter ShapeOf(
        IrConstructorParameter parameter,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new(
            IrToTsTypeMapper.MapParameterType(parameter.Parameter, typeOverrides),
            parameter.Parameter.CanBeOmitted,
            parameter.Parameter.IsParams
        );

    private static TsExpression ForwardedArgument(TsParameter parameter)
    {
        var argument = new TsIdentifier(parameter.Name);

        return parameter.Rest ? new TsSpreadExpression(argument) : argument;
    }

    /// <summary>The class as the factory returns it: <c>Box&lt;T&gt;</c>.</summary>
    private static TsNamedType SelfType(
        string typeName,
        IReadOnlyList<TsTypeParameter>? typeParameters
    ) =>
        new(
            typeName,
            typeParameters?.Select(parameter => (TsType)new TsNamedType(parameter.Name)).ToList()
        );

    /// <summary>The overload's own visibility under #445's member mapping (FR-011).</summary>
    private static TsMemberVisibility VisibilityOf(IrConstructorDeclaration overload) =>
        IrToTsVisibilityMapper.Map(overload.Visibility);
}
