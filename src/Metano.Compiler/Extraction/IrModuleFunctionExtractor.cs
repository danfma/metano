using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts <see cref="IrModuleFunction"/>s from a C# static class. Covers
/// <c>[ExportedAsModule]</c> classes (each ordinary public method becomes a
/// module-level function), classic <c>this T</c> extension methods (Roslyn
/// already exposes the receiver as the first parameter), classic-style
/// extension properties (folded into a getter-shaped function), and C# 14
/// <c>extension(R r) { ... }</c> blocks (each member inside becomes a
/// function with the receiver in the leading position).
/// <c>[ModuleEntryPoint]</c> bodies are not emitted here — the caller
/// unwraps them as top-level statements.
/// </summary>
public static class IrModuleFunctionExtractor
{
    /// <summary>
    /// Returns the module-level functions that should be emitted for
    /// <paramref name="type"/>, or an empty list if the class contributes nothing
    /// to the module form. Filtering of the containing class (e.g., checking
    /// <c>[ExportedAsModule]</c>) is the caller's responsibility; this extractor
    /// only walks the members it is given.
    /// </summary>
    public static IReadOnlyList<IrModuleFunction> Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var functions = new List<IrModuleFunction>();
        foreach (var member in type.GetMembers())
        {
            if (SymbolHelper.HasIgnore(member, target))
                continue;

            // Plain static methods (and classic `this T` extensions, which
            // Roslyn exposes the same way).
            if (
                member is IMethodSymbol method
                && method.MethodKind == MethodKind.Ordinary
                && method.DeclaredAccessibility == Accessibility.Public
            )
            {
                // [Emit] methods are inlined at the call site — no standalone
                // function should be emitted for them.
                if (SymbolHelper.HasEmit(method))
                    continue;
                // Skip property accessors. Classic-style extension properties
                // come through as methods whose AssociatedSymbol is the
                // property; they're emitted separately via
                // ConvertClassicExtensionProperty. C# 14 `extension(R r)`
                // blocks surface their property getters as plain methods
                // (`get_Prop`) without an associated symbol — the real
                // property is rendered via ExtractFromExtensionBlock, so we
                // also skip the bare `get_*` / `set_*` form here to avoid
                // emitting duplicate empty-body functions.
                if (method.AssociatedSymbol is IPropertySymbol)
                    continue;
                if (
                    method.Name.StartsWith("get_", StringComparison.Ordinal)
                    || method.Name.StartsWith("set_", StringComparison.Ordinal)
                )
                    continue;
                functions.Add(ConvertMethod(method, originResolver, compilation));
                continue;
            }

            // Classic-style extension properties surface as IPropertySymbol
            // with a parameter list (the receiver). Fold each into a
            // getter-shaped module function — the first parameter is the
            // receiver, the body comes from the property's getter.
            if (
                member is IPropertySymbol prop
                && prop.Parameters.Length > 0
                && prop.DeclaredAccessibility == Accessibility.Public
                && !prop.IsImplicitlyDeclared
                && compilation is not null
            )
            {
                var propFn = ConvertClassicExtensionProperty(prop, originResolver, compilation);
                if (propFn is not null)
                    functions.Add(propFn);
            }
        }

        // C# 14 `extension(R receiver) { ... }` blocks — Roslyn doesn't fold
        // their members into the containing type's member list the way classic
        // `this T` extensions do, so we walk the syntax directly. Each method
        // inside a block becomes an IrModuleFunction with the receiver as its
        // first parameter; extension properties collapse to getter-shaped
        // functions with only the receiver in the parameter list.
        if (compilation is not null)
        {
            foreach (var syntaxRef in type.DeclaringSyntaxReferences)
            {
                foreach (var node in syntaxRef.GetSyntax().DescendantNodes())
                {
                    if (node.Kind().ToString() != "ExtensionBlockDeclaration")
                        continue;
                    ExtractFromExtensionBlock(node, compilation, originResolver, functions);
                }
            }
        }

        return functions;
    }

    /// <summary>
    /// Classic-style extension property — <c>public static T Foo(this X x) {…}</c>
    /// surfaces on Roslyn as an <see cref="IPropertySymbol"/> with the receiver
    /// in <see cref="IPropertySymbol.Parameters"/>. Emit it as a getter-shaped
    /// function: receiver + any extra parameters up front, body pulled from
    /// the property's get accessor.
    /// </summary>
    private static IrModuleFunction? ConvertClassicExtensionProperty(
        IPropertySymbol prop,
        IrTypeOriginResolver? originResolver,
        Compilation compilation
    )
    {
        var getter = prop.GetMethod;
        if (getter is null)
            return null;
        var syntax = getter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is null)
            return null;
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var extractor = new IrStatementExtractor(model, originResolver);

        IReadOnlyList<IrStatement>? body = syntax switch
        {
            AccessorDeclarationSyntax a => extractor.ExtractBody(a.Body, a.ExpressionBody, false),
            ArrowExpressionClauseSyntax arrow => new[]
            {
                (IrStatement)
                    new IrReturnStatement(
                        new IrExpressionExtractor(model, originResolver).Extract(arrow.Expression)
                    ),
            },
            _ => null,
        };

        var parameters = prop
            .Parameters.Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, originResolver)
            ))
            .ToList();

        return new IrModuleFunction(
            Name: prop.Name,
            Parameters: parameters,
            ReturnType: IrTypeRefMapper.Map(prop.Type, originResolver),
            Body: body,
            Semantics: new IrMethodSemantics(false, false, IsExtension: true, false, null),
            TypeParameters: null,
            Attributes: IrAttributeExtractor.Extract(prop)
        );
    }

    private static void ExtractFromExtensionBlock(
        SyntaxNode extensionBlock,
        Compilation compilation,
        IrTypeOriginResolver? originResolver,
        List<IrModuleFunction> acc
    )
    {
        var paramListProp = extensionBlock.GetType().GetProperty("ParameterList");
        var membersProp = extensionBlock.GetType().GetProperty("Members");
        if (paramListProp?.GetValue(extensionBlock) is not ParameterListSyntax paramList)
            return;
        if (
            membersProp?.GetValue(extensionBlock) is not SyntaxList<MemberDeclarationSyntax> members
        )
            return;
        if (paramList.Parameters.Count == 0)
            return;

        var receiverSyntax = paramList.Parameters[0];
        var model = compilation.GetSemanticModel(extensionBlock.SyntaxTree);
        var receiverTypeSymbol = receiverSyntax.Type is null
            ? null
            : model.GetTypeInfo(receiverSyntax.Type).Type;
        if (receiverTypeSymbol is null)
            return;
        var receiver = new IrParameter(
            receiverSyntax.Identifier.ValueText,
            IrTypeRefMapper.Map(receiverTypeSymbol, originResolver)
        );

        foreach (var member in members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax methodSyntax:
                    var methodSymbol = model.GetDeclaredSymbol(methodSyntax) as IMethodSymbol;
                    if (methodSymbol is null)
                        continue;
                    if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    acc.Add(
                        ConvertExtensionMethod(methodSymbol, receiver, originResolver, compilation)
                    );
                    break;

                case PropertyDeclarationSyntax propSyntax:
                    var propSymbol = model.GetDeclaredSymbol(propSyntax) as IPropertySymbol;
                    if (propSymbol is null)
                        continue;
                    if (propSymbol.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    var propFn = ConvertExtensionProperty(
                        propSymbol,
                        propSyntax,
                        receiver,
                        model,
                        originResolver
                    );
                    if (propFn is not null)
                        acc.Add(propFn);
                    break;
            }
        }
    }

    private static IrModuleFunction ConvertExtensionMethod(
        IMethodSymbol method,
        IrParameter receiver,
        IrTypeOriginResolver? originResolver,
        Compilation compilation
    )
    {
        var parameters = new List<IrParameter> { receiver };
        parameters.AddRange(
            method.Parameters.Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, originResolver),
                HasDefaultValue: p.HasExplicitDefaultValue,
                DefaultValue: ExtractDefaultValue(p, compilation, originResolver)
            ))
        );

        var hasYield = DetectYield(method);
        var returnType = hasYield
            ? IrTypeRefMapper.MapForGeneratorReturn(method.ReturnType, originResolver)
            : IrTypeRefMapper.Map(method.ReturnType, originResolver);

        var semantics = new IrMethodSemantics(
            IsAsync: hasYield ? false : method.IsAsync,
            IsGenerator: hasYield,
            IsExtension: true,
            IsOperator: false,
            OperatorKind: null
        );

        var typeParameters =
            method.TypeParameters.Length > 0
                ? method
                    .TypeParameters.Select(tp => new IrTypeParameter(
                        tp.Name,
                        tp.ConstraintTypes.Length > 0
                            ? tp
                                .ConstraintTypes.Select(t => IrTypeRefMapper.Map(t, originResolver))
                                .ToList()
                            : null
                    ))
                    .ToList()
                : null;

        var body = TryExtractBody(method, compilation, originResolver);

        return new IrModuleFunction(
            Name: method.Name,
            Parameters: parameters,
            ReturnType: returnType,
            Body: body,
            Semantics: semantics,
            TypeParameters: typeParameters,
            Attributes: IrAttributeExtractor.Extract(method)
        );
    }

    private static IrModuleFunction? ConvertExtensionProperty(
        IPropertySymbol prop,
        PropertyDeclarationSyntax syntax,
        IrParameter receiver,
        SemanticModel model,
        IrTypeOriginResolver? originResolver
    )
    {
        var extractor = new IrStatementExtractor(model, originResolver);
        IReadOnlyList<IrStatement>? body;
        if (syntax.ExpressionBody is not null)
        {
            var expr = new IrExpressionExtractor(model, originResolver).Extract(
                syntax.ExpressionBody.Expression
            );
            body = [new IrReturnStatement(expr)];
        }
        else if (
            syntax.AccessorList?.Accessors.FirstOrDefault(a =>
                a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
            ) is
            { } getter
        )
        {
            body = extractor.ExtractBody(getter.Body, getter.ExpressionBody, isVoid: false);
        }
        else
        {
            return null;
        }

        return new IrModuleFunction(
            Name: prop.Name,
            Parameters: [receiver],
            ReturnType: IrTypeRefMapper.Map(prop.Type, originResolver),
            Body: body,
            Semantics: new IrMethodSemantics(false, false, IsExtension: true, false, null),
            TypeParameters: null,
            Attributes: IrAttributeExtractor.Extract(prop)
        );
    }

    private static IrModuleFunction ConvertMethod(
        IMethodSymbol method,
        IrTypeOriginResolver? originResolver,
        Compilation? compilation
    )
    {
        var parameters = method
            .Parameters.Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, originResolver),
                HasDefaultValue: p.HasExplicitDefaultValue,
                DefaultValue: ExtractDefaultValue(p, compilation, originResolver)
            ))
            .ToList();

        // `yield` in the body makes this a generator. Return type goes through
        // the generator-aware mapper (so `IEnumerable<T>` lowers to
        // `IrGeneratorTypeRef<T>`) and the async flag is forced off — the TS
        // backend doesn't represent async generators in this path.
        var hasYield = DetectYield(method);
        var returnType = hasYield
            ? IrTypeRefMapper.MapForGeneratorReturn(method.ReturnType, originResolver)
            : IrTypeRefMapper.Map(method.ReturnType, originResolver);

        var semantics = new IrMethodSemantics(
            IsAsync: hasYield ? false : method.IsAsync,
            IsGenerator: hasYield,
            IsExtension: method.IsExtensionMethod,
            IsOperator: false,
            OperatorKind: null
        );

        var typeParameters =
            method.TypeParameters.Length > 0
                ? method
                    .TypeParameters.Select(tp => new IrTypeParameter(
                        tp.Name,
                        tp.ConstraintTypes.Length > 0
                            ? tp
                                .ConstraintTypes.Select(t => IrTypeRefMapper.Map(t, originResolver))
                                .ToList()
                            : null
                    ))
                    .ToList()
                : null;

        var body = compilation is not null
            ? TryExtractBody(method, compilation, originResolver)
            : null;

        return new IrModuleFunction(
            Name: method.Name,
            Parameters: parameters,
            ReturnType: returnType,
            Body: body,
            Semantics: semantics,
            TypeParameters: typeParameters,
            Attributes: IrAttributeExtractor.Extract(method)
        );
    }

    private static bool DetectYield(IMethodSymbol method)
    {
        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        return syntax?.DescendantNodes().OfType<YieldStatementSyntax>().Any() ?? false;
    }

    private static IReadOnlyList<IrStatement>? TryExtractBody(
        IMethodSymbol method,
        Compilation compilation,
        IrTypeOriginResolver? originResolver
    )
    {
        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (syntax is null || (syntax.Body is null && syntax.ExpressionBody is null))
            return null;
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var extractor = new IrStatementExtractor(model, originResolver);
        return extractor.ExtractBody(syntax.Body, syntax.ExpressionBody, method.ReturnsVoid);
    }

    /// <summary>
    /// Parses a parameter's default-value syntax into IR — mirrors the helper
    /// on <see cref="IrMethodExtractor"/> so module-function parameters keep
    /// their defaults on every target that renders them.
    /// </summary>
    private static IrExpression? ExtractDefaultValue(
        IParameterSymbol parameter,
        Compilation? compilation,
        IrTypeOriginResolver? originResolver
    )
    {
        if (!parameter.HasExplicitDefaultValue || compilation is null)
            return null;
        var syntax =
            parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ParameterSyntax;
        var defaultExpression = syntax?.Default?.Value;
        if (defaultExpression is null)
            return null;
        var model = compilation.GetSemanticModel(syntax!.SyntaxTree);
        return new IrExpressionExtractor(model, originResolver).Extract(defaultExpression);
    }
}
