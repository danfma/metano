using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Builds an <see cref="IrFieldDeclaration"/> from a Roslyn <see cref="IFieldSymbol"/>:
/// shape (type, visibility, static / readonly / const flags, attributes) plus the
/// initializer expression when a <see cref="Compilation"/> is supplied. Shared by
/// the class extractor (fields of an emitted type) and the module-variable
/// extractor (fields of a containerless static class).
/// </summary>
public static class IrFieldExtractor
{
    /// <summary>
    /// Returns <c>null</c> for fields that are not declared as fields of their own:
    /// compiler-generated property backing fields and enum members.
    /// </summary>
    public static IrFieldDeclaration? TryExtract(
        IFieldSymbol field,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        if (field.AssociatedSymbol is IPropertySymbol)
            return null;

        if (field.ContainingType.TypeKind == TypeKind.Enum)
            return null;

        var initializer = compilation is not null
            ? TryExtractInitializer(field, compilation, originResolver, target)
            : null;

        return new IrFieldDeclaration(
            field.Name,
            IrVisibilityMapper.Map(field.DeclaredAccessibility),
            field.IsStatic,
            IrTypeRefMapper.Map(field.Type, originResolver, target),
            // A C# `const` is read-only too; Roslyn reports it through IsConst only.
            IsReadonly: field.IsReadOnly || field.IsConst,
            Initializer: initializer,
            IsConstant: field.HasConstant(),
            IsRequired: field.IsRequired
        )
        {
            Attributes = IrAttributeExtractor.Extract(field),
        };
    }

    private static IrExpression? TryExtractInitializer(
        IFieldSymbol field,
        Compilation compilation,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var declarator =
            field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as VariableDeclaratorSyntax;

        if (declarator?.Initializer is null)
            return null;

        var model = compilation.GetSemanticModel(declarator.SyntaxTree);
        var expr = new IrExpressionExtractor(model, originResolver, target);

        return expr.ExtractStandalone(declarator.Initializer.Value);
    }
}
