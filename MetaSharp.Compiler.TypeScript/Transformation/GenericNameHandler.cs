using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Handles a generic type name appearing as an expression
/// (e.g., <c>OperationResult&lt;Issue&gt;</c> in <c>OperationResult&lt;Issue&gt;.Ok(...)</c>).
///
/// TypeScript erases generic arguments at runtime, so the type name alone is enough — the
/// arguments are dropped. PascalCase is preserved when the symbol resolves to a type;
/// otherwise the identifier text is used as-is.
/// </summary>
public sealed class GenericNameHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression Transform(GenericNameSyntax genericName)
    {
        var symbol = _parent.Model.GetSymbolInfo(genericName).Symbol;

        // If it resolves to a type, keep PascalCase
        if (symbol is INamedTypeSymbol)
            return new TsIdentifier(genericName.Identifier.Text);

        // Check if the semantic model can resolve to a type via SymbolInfo
        var typeInfo = _parent.Model.GetTypeInfo(genericName);
        if (typeInfo.Type is not null)
            return new TsIdentifier(typeInfo.Type.Name);

        // Fallback — use the identifier text as-is (PascalCase for types)
        return new TsIdentifier(genericName.Identifier.Text);
    }
}
