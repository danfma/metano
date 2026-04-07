using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Handles dotted member access expressions (<c>obj.Property</c>, <c>Type.Member</c>).
///
/// Two main concerns:
/// <list type="bullet">
///   <item>BCL mappings — when the symbol matches a known BCL member,
///   <see cref="BclMapper.TryMap"/> rewrites the access to its TypeScript equivalent.</item>
///   <item>Casing — enum members keep their PascalCase form, while everything else is
///   camelCased to match the rest of the TypeScript output.</item>
/// </list>
/// </summary>
public sealed class MemberAccessHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression Transform(MemberAccessExpressionSyntax member)
    {
        // Check for BCL mappings
        var symbol = _parent.Model.GetSymbolInfo(member).Symbol;
        if (symbol is not null)
        {
            var mapped = BclMapper.TryMap(symbol, member, _parent);
            if (mapped is not null)
                return mapped;
        }

        var obj = _parent.TransformExpression(member.Expression);

        // Enum members and constants → keep PascalCase
        var memberName = symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum }
            ? member.Name.Identifier.Text
            : TypeScriptNaming.ToCamelCase(member.Name.Identifier.Text);

        return new TsPropertyAccess(obj, memberName);
    }
}
