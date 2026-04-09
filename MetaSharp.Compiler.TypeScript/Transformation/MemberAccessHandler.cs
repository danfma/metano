using MetaSharp.Compiler;
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

        // Nullable<T>.Value / .HasValue elision: in TS the value carries no envelope —
        // a `T?` value is just `T | null`. After the user's null check, the value IS
        // the value. <c>.Value</c> on a Nullable lowers to the receiver as-is;
        // <c>.HasValue</c> becomes a null comparison so the meaning is preserved.
        var receiverType = _parent.Model.GetTypeInfo(member.Expression).Type;
        if (receiverType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            var memberText = member.Name.Identifier.Text;
            if (memberText == "Value")
                return _parent.TransformExpression(member.Expression);
            if (memberText == "HasValue")
                return new TsBinaryExpression(
                    _parent.TransformExpression(member.Expression),
                    "!==",
                    new TsLiteral("null"));
        }

        var obj = _parent.TransformExpression(member.Expression);

        // Enum members and constants → keep PascalCase
        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum })
            return new TsPropertyAccess(obj, member.Name.Identifier.Text);

        // [Name("override")] takes precedence — verbatim, no camelCase, no escape.
        // This matters for cases like `[Name("delete")]` on a binding where the
        // camelCase pipeline would otherwise escape the JS reserved word to
        // `delete_` (correct for variable names, but the user explicitly opted into
        // a property name and reserved words ARE valid in property position).
        var nameOverride = symbol is null ? null : SymbolHelper.GetNameOverride(symbol);
        if (nameOverride is not null)
            return new TsPropertyAccess(obj, nameOverride);

        var memberName = TypeScriptNaming.ToCamelCase(member.Name.Identifier.Text);
        return new TsPropertyAccess(obj, memberName);
    }
}
