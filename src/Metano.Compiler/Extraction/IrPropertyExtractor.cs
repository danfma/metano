using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Builds an <see cref="IrPropertyDeclaration"/> from a Roslyn <see cref="IPropertySymbol"/>.
/// Extracts the shape (type, accessors, visibility, per-accessor visibility, static flag,
/// attributes) plus semantic flags. When a <see cref="Compilation"/> is supplied, also
/// fills in getter/setter bodies and initializer expressions via
/// <see cref="IrStatementExtractor"/> / <see cref="IrExpressionExtractor"/>.
/// </summary>
public static class IrPropertyExtractor
{
    public static IrPropertyDeclaration Extract(
        IPropertySymbol prop,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var accessors = ResolveAccessors(prop);

        var setterVisibility =
            prop.SetMethod is not null
            && prop.SetMethod.DeclaredAccessibility != prop.DeclaredAccessibility
                ? IrVisibilityMapper.Map(prop.SetMethod.DeclaredAccessibility)
                : (IrVisibility?)null;

        var syntax =
            prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as PropertyDeclarationSyntax;

        var semantics = BuildSemantics(prop, syntax);

        IReadOnlyList<IrStatement>? getterBody = null;
        IReadOnlyList<IrStatement>? setterBody = null;
        IrExpression? initializer = null;

        if (compilation is not null && syntax is not null)
        {
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var stmtExtractor = new IrStatementExtractor(model, originResolver, target);
            var exprExtractor = new IrExpressionExtractor(model, originResolver, target);

            // Expression-bodied property: the whole `=> expr;` is the getter.
            if (syntax.ExpressionBody is not null)
            {
                getterBody =
                [
                    new IrReturnStatement(exprExtractor.Extract(syntax.ExpressionBody.Expression)),
                ];
            }
            else if (syntax.AccessorList is not null)
            {
                var getter = syntax.AccessorList.Accessors.FirstOrDefault(a =>
                    a.IsKind(SyntaxKind.GetAccessorDeclaration)
                );
                var setter = syntax.AccessorList.Accessors.FirstOrDefault(a =>
                    a.IsKind(SyntaxKind.SetAccessorDeclaration)
                );

                if (
                    getter is not null
                    && (getter.Body is not null || getter.ExpressionBody is not null)
                )
                {
                    getterBody = stmtExtractor.ExtractBody(
                        getter.Body,
                        getter.ExpressionBody,
                        isVoid: false
                    );
                }
                if (
                    setter is not null
                    && (setter.Body is not null || setter.ExpressionBody is not null)
                )
                {
                    setterBody = stmtExtractor.ExtractBody(
                        setter.Body,
                        setter.ExpressionBody,
                        isVoid: true
                    );
                }
            }

            if (syntax.Initializer is not null)
                initializer = exprExtractor.Extract(syntax.Initializer.Value);
        }

        return new IrPropertyDeclaration(
            prop.Name,
            IrVisibilityMapper.Map(prop.DeclaredAccessibility),
            prop.IsStatic,
            IrTypeRefMapper.Map(prop.Type, originResolver, target),
            accessors,
            SetterVisibility: setterVisibility,
            Initializer: initializer,
            GetterBody: getterBody,
            SetterBody: setterBody,
            Semantics: semantics
        )
        {
            Attributes = IrAttributeExtractor.Extract(prop),
        };
    }

    private static IrPropertyAccessors ResolveAccessors(IPropertySymbol prop)
    {
        if (prop.SetMethod is null)
            return IrPropertyAccessors.GetOnly;
        if (prop.SetMethod.IsInitOnly)
            return IrPropertyAccessors.GetInit;
        return IrPropertyAccessors.GetSet;
    }

    private static IrPropertySemantics BuildSemantics(
        IPropertySymbol prop,
        PropertyDeclarationSyntax? syntax
    )
    {
        var hasGetterBody = DetectGetterBody(syntax);
        var hasSetterBody = DetectSetterBody(syntax);
        var hasInitializer = syntax?.Initializer is not null;

        return new IrPropertySemantics(
            HasGetterBody: hasGetterBody,
            HasSetterBody: hasSetterBody,
            HasInitializer: hasInitializer,
            IsAbstract: prop.IsAbstract,
            IsVirtual: prop.IsVirtual,
            IsOverride: prop.IsOverride,
            IsSealed: prop.IsSealed
        );
    }

    private static bool DetectGetterBody(PropertyDeclarationSyntax? syntax)
    {
        if (syntax is null)
            return false;
        if (syntax.ExpressionBody is not null)
            return true;
        return syntax.AccessorList?.Accessors.Any(a =>
                a.IsKind(SyntaxKind.GetAccessorDeclaration)
                && (a.Body is not null || a.ExpressionBody is not null)
            )
            ?? false;
    }

    private static bool DetectSetterBody(PropertyDeclarationSyntax? syntax)
    {
        if (syntax is null)
            return false;
        return syntax.AccessorList?.Accessors.Any(a =>
                a.IsKind(SyntaxKind.SetAccessorDeclaration)
                && (a.Body is not null || a.ExpressionBody is not null)
            )
            ?? false;
    }
}
