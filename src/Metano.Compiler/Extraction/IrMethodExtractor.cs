using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Builds an <see cref="IrMethodDeclaration"/> from a Roslyn <see cref="IMethodSymbol"/>.
/// Extracts the full signature (name, parameters, return type, type parameters) plus
/// semantic flags (async, generator, operator kind, virtual/override/abstract/sealed).
/// Method bodies are left null and filled in when statement extraction is available.
/// </summary>
public static class IrMethodExtractor
{
    public static IrMethodDeclaration Extract(
        IMethodSymbol method,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var hasYield = DetectYield(method);

        var returnType = hasYield
            ? IrTypeRefMapper.MapForGeneratorReturn(method.ReturnType, originResolver)
            : IrTypeRefMapper.Map(method.ReturnType, originResolver);

        var parameters = method
            .Parameters.Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, originResolver),
                HasDefaultValue: p.HasExplicitDefaultValue,
                DefaultValue: ExtractDefaultValue(p, compilation, originResolver),
                IsOptional: p.HasOptional(),
                IsConstant: p.HasConstant()
            ))
            .ToList();

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

        var semantics = new IrMethodSemantics(
            IsAsync: hasYield ? false : method.IsAsync,
            IsGenerator: hasYield,
            IsExtension: method.IsExtensionMethod,
            IsOperator: IsOperatorKind(method.MethodKind),
            OperatorKind: ResolveOperatorKind(method),
            IsAbstract: method.IsAbstract,
            IsVirtual: method.IsVirtual,
            IsOverride: method.IsOverride,
            IsSealed: method.IsSealed,
            IsEmitTemplate: SymbolHelper.HasEmit(method)
        );

        var body = compilation is not null
            ? TryExtractBody(method, compilation, originResolver, target)
            : null;

        return new IrMethodDeclaration(
            method.Name,
            IrVisibilityMapper.Map(method.DeclaredAccessibility),
            method.IsStatic,
            parameters,
            returnType,
            Body: body,
            Semantics: semantics,
            TypeParameters: typeParameters
        )
        {
            Attributes = IrAttributeExtractor.Extract(method),
        };
    }

    private static IReadOnlyList<IrStatement>? TryExtractBody(
        IMethodSymbol method,
        Compilation compilation,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        // Method, operator, conversion-operator, and destructor declarations
        // all derive from BaseMethodDeclarationSyntax, which carries the Body
        // member; ArrowExpressionClause sits on the leaf types so we read it
        // through the concrete shapes.
        var (body, expressionBody) = syntax switch
        {
            MethodDeclarationSyntax m => ((BlockSyntax?)m.Body, m.ExpressionBody),
            OperatorDeclarationSyntax o => ((BlockSyntax?)o.Body, o.ExpressionBody),
            ConversionOperatorDeclarationSyntax c => ((BlockSyntax?)c.Body, c.ExpressionBody),
            _ => (null, null),
        };
        if (body is null && expressionBody is null)
            return null;

        var model = compilation.GetSemanticModel(syntax!.SyntaxTree);
        var extractor = new IrStatementExtractor(model, originResolver, target);
        return extractor.ExtractBody(body, expressionBody, method.ReturnsVoid);
    }

    /// <summary>
    /// Parses a parameter's default-value syntax into IR so backends that render
    /// defaults (like the Dart target) can reproduce them. Returns null when the
    /// parameter has no default, when no compilation is supplied (shape-only mode),
    /// or when the declaring syntax isn't reachable (referenced-assembly methods).
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

    private static bool DetectYield(IMethodSymbol method)
    {
        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        return syntax?.DescendantNodes().OfType<YieldStatementSyntax>().Any() ?? false;
    }

    private static bool IsOperatorKind(MethodKind kind) =>
        kind is MethodKind.UserDefinedOperator or MethodKind.Conversion;

    private static string? ResolveOperatorKind(IMethodSymbol method)
    {
        if (method.MethodKind == MethodKind.UserDefinedOperator)
        {
            // e.g. "op_Addition" -> "Addition"
            return method.Name.StartsWith("op_") ? method.Name[3..] : method.Name;
        }
        if (method.MethodKind == MethodKind.Conversion)
        {
            // op_Implicit / op_Explicit
            return method.Name.StartsWith("op_") ? method.Name[3..] : method.Name;
        }
        return null;
    }
}
