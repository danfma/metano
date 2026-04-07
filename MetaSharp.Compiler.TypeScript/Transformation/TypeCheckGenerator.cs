using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Generates the runtime per-parameter type checks emitted by the constructor and method
/// overload dispatchers (e.g., <c>isInt32(args[0])</c>, <c>args[0] instanceof Foo</c>,
/// <c>typeof args[0] === "string"</c>).
///
/// Pure / stateless: each call returns the appropriate <see cref="TsExpression"/> for the
/// given parameter type. The numeric primitive helpers (<c>isInt32</c>, <c>isUInt16</c>, …)
/// are runtime functions provided by <c>@meta-sharp/runtime</c>.
/// </summary>
public static class TypeCheckGenerator
{
    /// <summary>
    /// Builds <c>argCheckExpression(args[argIndex])</c> for the given C# parameter type.
    /// </summary>
    /// <param name="csharpType">The C# parameter type to check.</param>
    /// <param name="argIndex">Position in the dispatcher's <c>args</c> rest parameter.</param>
    /// <param name="assemblyWideTranspile">Whether <c>[TranspileAssembly]</c> is in effect.</param>
    /// <param name="currentAssembly">Current compilation's assembly (used to scope assembly-wide transpile).</param>
    public static TsExpression GenerateForParam(
        ITypeSymbol csharpType,
        int argIndex,
        bool assemblyWideTranspile,
        IAssemblySymbol? currentAssembly)
    {
        var argAccess = new TsIdentifier($"args[{argIndex}]");
        var fullName = csharpType.ToDisplayString();

        return fullName switch
        {
            "char" => new TsCallExpression(new TsIdentifier("isChar"), [argAccess]),
            "string" => new TsCallExpression(new TsIdentifier("isString"), [argAccess]),
            "byte" => new TsCallExpression(new TsIdentifier("isByte"), [argAccess]),
            "sbyte" => new TsCallExpression(new TsIdentifier("isSByte"), [argAccess]),
            "short" or "System.Int16" => new TsCallExpression(new TsIdentifier("isInt16"), [argAccess]),
            "ushort" or "System.UInt16" => new TsCallExpression(new TsIdentifier("isUInt16"), [argAccess]),
            "int" or "System.Int32" => new TsCallExpression(new TsIdentifier("isInt32"), [argAccess]),
            "uint" or "System.UInt32" => new TsCallExpression(new TsIdentifier("isUInt32"), [argAccess]),
            "long" or "System.Int64" => new TsCallExpression(new TsIdentifier("isInt64"), [argAccess]),
            "ulong" or "System.UInt64" => new TsCallExpression(new TsIdentifier("isUInt64"), [argAccess]),
            "float" or "System.Single" => new TsCallExpression(new TsIdentifier("isFloat32"), [argAccess]),
            "double" or "System.Double" => new TsCallExpression(new TsIdentifier("isFloat64"), [argAccess]),
            "bool" or "System.Boolean" => new TsCallExpression(new TsIdentifier("isBool"), [argAccess]),
            "decimal" or "System.Decimal" => new TsCallExpression(new TsIdentifier("isFloat64"), [argAccess]),
            "System.Numerics.BigInteger" => new TsCallExpression(new TsIdentifier("isBigInt"), [argAccess]),

            // Enums with [StringEnum] → exhaustive value check
            _ when csharpType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                && SymbolHelper.HasStringEnum(enumType) =>
                GenerateStringEnumCheck(enumType, argAccess),

            // Numeric enums → typeof number
            _ when csharpType is INamedTypeSymbol { TypeKind: TypeKind.Enum } =>
                new TsCallExpression(new TsIdentifier("isInt32"), [argAccess]),

            // Interfaces → shape check (typeof object, can't use instanceof)
            _ when csharpType is INamedTypeSymbol { TypeKind: TypeKind.Interface } =>
                new TsBinaryExpression(
                    new TsUnaryExpression("typeof ", argAccess),
                    "===", new TsStringLiteral("object")),

            // InlineWrapper structs → typeof check on the underlying primitive
            _ when csharpType is INamedTypeSymbol inlineType
                && SymbolHelper.HasInlineWrapper(inlineType)
                && TypeTransformer.TryGetInlineWrapperPrimitiveType(inlineType, out var primType) =>
                GenerateInlineWrapperCheck(primType, argAccess),

            // Classes/records → instanceof
            _ when csharpType is INamedTypeSymbol named
                && SymbolHelper.IsTranspilable(named, assemblyWideTranspile, currentAssembly) =>
                new TsBinaryExpression(argAccess, "instanceof", new TsIdentifier(named.Name)),

            // Arrays
            _ when csharpType is IArrayTypeSymbol =>
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                    [argAccess]),

            // Default: typeof check based on TS type
            _ => new TsBinaryExpression(
                new TsUnaryExpression("typeof ", argAccess),
                "===",
                new TsStringLiteral("object"))
        };
    }

    /// <summary>
    /// <c>(value === "USD" || value === "EUR" || …)</c> for an enum tagged with [StringEnum].
    /// </summary>
    private static TsExpression GenerateStringEnumCheck(INamedTypeSymbol enumType, TsExpression argAccess)
    {
        var members = enumType.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .ToList();

        if (members.Count == 0)
            return new TsCallExpression(new TsIdentifier("isString"), [argAccess]);

        TsExpression check = members
            .Select<IFieldSymbol, TsExpression>(m =>
            {
                var name = SymbolHelper.GetNameOverride(m) ?? m.Name;
                return new TsBinaryExpression(argAccess, "===", new TsStringLiteral(name));
            })
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

        return new TsParenthesized(check);
    }

    /// <summary>
    /// <c>typeof value === "string|number|boolean|bigint"</c> for inline wrapper structs.
    /// </summary>
    private static TsExpression GenerateInlineWrapperCheck(TsType primitiveType, TsExpression argAccess)
    {
        var jsType = primitiveType switch
        {
            TsStringType => "string",
            TsNumberType => "number",
            TsBooleanType => "boolean",
            TsBigIntType => "bigint",
            _ => "object",
        };
        return new TsBinaryExpression(
            new TsUnaryExpression("typeof ", argAccess),
            "===",
            new TsStringLiteral(jsType));
    }
}
