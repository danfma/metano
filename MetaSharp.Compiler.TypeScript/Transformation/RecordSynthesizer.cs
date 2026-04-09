using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Synthesizes the value-equality plumbing emitted for C# records:
/// <c>equals(other)</c>, <c>hashCode()</c>, <c>with(overrides)</c>, plus the
/// <see cref="MakeSelfType"/> helper used by both record and non-record class output.
///
/// Pure / stateless: each method takes the constructor parameter list (and the type, when
/// the type's name is needed) and returns a TypeScript AST node.
/// </summary>
public static class RecordSynthesizer
{
    public static TsMethodMember GenerateEquals(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> ctorParams
    )
    {
        // equals(other: any): boolean {
        //   return other instanceof Type && this.x === other.x && ...
        // }
        TsExpression condition = new TsBinaryExpression(
            new TsIdentifier("other"),
            "instanceof",
            new TsIdentifier(TypeTransformer.GetTsTypeName(type))
        );

        foreach (var param in ctorParams)
        {
            condition = new TsBinaryExpression(
                condition,
                "&&",
                new TsBinaryExpression(
                    new TsPropertyAccess(new TsIdentifier("this"), param.Name),
                    "===",
                    new TsPropertyAccess(new TsIdentifier("other"), param.Name)
                )
            );
        }

        return new TsMethodMember(
            "equals",
            [new TsParameter("other", new TsAnyType())],
            new TsBooleanType(),
            [new TsReturnStatement(condition)]
        );
    }

    public static TsMethodMember GenerateHashCode(IReadOnlyList<TsConstructorParam> ctorParams)
    {
        // hashCode(): number {
        //   const hc = new HashCode();
        //   hc.add(this.x);
        //   hc.add(this.y);
        //   return hc.toHashCode();
        // }
        var body = new List<TsStatement>
        {
            new TsVariableDeclaration("hc", new TsNewExpression(new TsIdentifier("HashCode"), [])),
        };

        foreach (var param in ctorParams)
        {
            body.Add(
                new TsExpressionStatement(
                    new TsCallExpression(
                        new TsPropertyAccess(new TsIdentifier("hc"), "add"),
                        [new TsPropertyAccess(new TsIdentifier("this"), param.Name)]
                    )
                )
            );
        }

        body.Add(
            new TsReturnStatement(
                new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "toHashCode"), [])
            )
        );

        return new TsMethodMember("hashCode", [], new TsNumberType(), body);
    }

    public static TsMethodMember GenerateWith(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> ctorParams
    )
    {
        var selfType = MakeSelfType(type);
        var args = ctorParams
            .Select<TsConstructorParam, TsExpression>(p => new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("overrides?"), p.Name),
                "??",
                new TsPropertyAccess(new TsIdentifier("this"), p.Name)
            ))
            .ToList();

        return new TsMethodMember(
            "with",
            [new TsParameter("overrides?", new TsNamedType("Partial", [selfType]))],
            selfType,
            [
                new TsReturnStatement(
                    new TsNewExpression(new TsIdentifier(TypeTransformer.GetTsTypeName(type)), args)
                ),
            ]
        );
    }

    /// <summary>
    /// Creates a TsNamedType for the type including its type parameters.
    /// e.g., Pair&lt;K, V&gt; → TsNamedType("Pair", [TsNamedType("K"), TsNamedType("V")]).
    /// </summary>
    public static TsNamedType MakeSelfType(INamedTypeSymbol type)
    {
        var tsName = TypeTransformer.GetTsTypeName(type);
        if (type.TypeParameters.Length == 0)
            return new TsNamedType(tsName);

        var args = type
            .TypeParameters.Select<ITypeParameterSymbol, TsType>(tp => new TsNamedType(tp.Name))
            .ToList();

        return new TsNamedType(tsName, args);
    }
}
