using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Resolves a bare C# <see cref="IdentifierNameSyntax"/> to its TypeScript form.
///
/// The mapping depends on the symbol the name resolves to:
/// <list type="bullet">
///   <item>Type references → kept PascalCase (e.g., <c>IssueStatus</c>, <c>Guid</c>, <c>UserId</c>)</item>
///   <item>Instance fields/properties (when emitted inside an instance method) → qualified
///   with the parent's <see cref="ExpressionTransformer.SelfParameterName"/> (typically
///   <c>"this"</c>)</item>
///   <item>Static members of the containing type → <c>ClassName.member</c></item>
///   <item>Anything else → camelCased identifier</item>
/// </list>
///
/// Holds a reference to the parent <see cref="ExpressionTransformer"/> so it can read both
/// the semantic <see cref="ExpressionTransformer.Model"/> and the current
/// <see cref="ExpressionTransformer.SelfParameterName"/>.
/// </summary>
public sealed class IdentifierHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression Transform(IdentifierNameSyntax id)
    {
        var symbol = _parent.Model.GetSymbolInfo(id).Symbol;

        // Type references → keep PascalCase (e.g., IssueStatus, Guid, UserId).
        // When the type comes from a cross-package source, route through TypeMapper
        // so the origin metadata is captured and the import collector can emit the
        // corresponding import statement. We can't reuse TsNamedType (it's a TsType,
        // not a TsExpression), so we emit a TsTypeReference wrapper that the printer
        // treats identically to a bare identifier but the collector recognizes.
        if (symbol is INamedTypeSymbol named)
        {
            var mapped = TypeMapper.Map(named);
            if (mapped is TsNamedType { Origin: { } origin } namedType)
                return new TsTypeReference(namedType.Name, origin);
            return new TsIdentifier(id.Identifier.Text);
        }
        if (symbol is ITypeSymbol)
            return new TsIdentifier(id.Identifier.Text);

        var name = TypeScriptNaming.ToCamelCase(id.Identifier.Text);

        if (symbol is not null && symbol.ContainingType is not null)
        {
            // Instance members → this.name
            if (symbol is IPropertySymbol or IFieldSymbol && !symbol.IsStatic)
            {
                if (_parent.SelfParameterName is not null)
                    return new TsPropertyAccess(new TsIdentifier(_parent.SelfParameterName), name);
            }

            // Instance method → this.name
            if (symbol is IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary })
            {
                if (_parent.SelfParameterName is not null)
                    return new TsPropertyAccess(new TsIdentifier(_parent.SelfParameterName), name);
            }

            // Static method/property of the same class → ClassName.name
            if (symbol.IsStatic && symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol)
            {
                return new TsPropertyAccess(new TsIdentifier(symbol.ContainingType.Name), name);
            }
        }

        return new TsIdentifier(name);
    }
}
