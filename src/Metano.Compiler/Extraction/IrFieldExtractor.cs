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
        IrExtractionContext? options = null
    )
    {
        var context = options ?? IrExtractionContext.Empty;
        var originResolver = context.OriginResolver;
        var compilation = context.Compilation;
        var target = context.Target;

        if (field.AssociatedSymbol is IPropertySymbol)
            return null;

        if (field.ContainingType.TypeKind == TypeKind.Enum)
            return null;

        var initializer = compilation is not null
            ? TryExtractInitializer(field, compilation, options)
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
            IsRequired: field.IsRequired,
            IsAssignedByInitAccessor: IsAssignedByInitAccessor(field, compilation),
            IsCompileTimeConstant: field.IsConst
        )
        {
            Attributes = IrAttributeExtractor.Extract(field),
        };
    }

    private static bool IsAssignedByInitAccessor(IFieldSymbol field, Compilation? compilation) =>
        field.IsReadOnly
        && compilation is not null
        && CustomInitAccessor.WritesField(field, compilation);

    private static IrExpression? TryExtractInitializer(
        IFieldSymbol field,
        Compilation compilation,
        IrExtractionContext? options
    )
    {
        var declarator =
            field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as VariableDeclaratorSyntax;

        if (declarator?.Initializer is null)
            return null;

        var model = compilation.GetSemanticModel(declarator.SyntaxTree);
        var expr = new IrExpressionExtractor(model, options);

        return expr.ExtractStandalone(declarator.Initializer.Value);
    }
}
