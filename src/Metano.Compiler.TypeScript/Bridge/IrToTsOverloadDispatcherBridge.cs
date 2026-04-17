using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrMethodDeclaration"/> whose
/// <see cref="IrMethodDeclaration.Overloads"/> carries the overload set into the
/// canonical TypeScript dispatcher shape:
/// <list type="number">
///   <item>A private fast-path method per overload (real body via
///   <see cref="IrToTsStatementBridge"/>), named uniquely within the group.</item>
///   <item>A public dispatcher method taking <c>...args: unknown[]</c> that
///   branches on argument count + runtime type checks and delegates to the
///   matching fast path with cast arguments.</item>
/// </list>
/// </summary>
public static class IrToTsOverloadDispatcherBridge
{
    /// <summary>
    /// Builds the dispatcher + fast-path members for a group of overloads.
    /// </summary>
    /// <param name="primary">
    /// The method that groups the overloads. Its
    /// <see cref="IrMethodDeclaration.Overloads"/> carries the siblings. When
    /// <c>Overloads</c> is null/empty this helper returns an empty list —
    /// callers should pick the regular single-method path in that case.
    /// </param>
    /// <param name="containingTypeName">
    /// Name of the class that hosts the overloads — used as the static
    /// receiver (<c>ClassName.fastPath(…)</c>). For instance overloads the
    /// dispatcher uses <c>this</c>, so the argument only matters when
    /// <paramref name="primary"/> is static.
    /// </param>
    /// <param name="bclRegistry">
    /// Optional BCL mapping registry threaded through to the body bridge.
    /// </param>
    public static IReadOnlyList<TsClassMember> BuildMethod(
        IrMethodDeclaration primary,
        string containingTypeName,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        var overloads = CollectOverloads(primary);
        if (overloads.Count < 2)
            return [];

        // Sort by descending parameter count so the most specific overload is
        // tried first in the dispatcher body (matches legacy behavior).
        var sorted = overloads.OrderByDescending(m => m.Parameters.Count).ToList();
        var primaryName = TypeScriptNaming.ToCamelCaseMember(primary.Name);
        var isStatic = primary.IsStatic;
        var isAsync = sorted.Any(m => m.Semantics.IsAsync);

        var commonReturn = ResolveCommonReturnType(sorted, isAsync);
        var overloadSignatures = sorted.Select(BuildOverloadSignature).ToList();
        var fastPathNames = ComputeFastPathNames(primaryName, sorted);

        var members = new List<TsClassMember>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var fastPath = BuildFastPathMember(sorted[i], fastPathNames[i], isStatic, bclRegistry);
            if (fastPath is not null)
                members.Add(fastPath);
        }

        var body = BuildDispatcherBody(sorted, fastPathNames, containingTypeName, isStatic);

        members.Add(
            new TsMethodMember(
                primaryName,
                [new TsParameter("...args", new TsNamedType("unknown[]"))],
                commonReturn,
                body,
                Static: isStatic,
                Async: isAsync,
                Accessibility: VisibilityToAccessibility(primary.Visibility),
                Overloads: overloadSignatures
            )
        );
        return members;
    }

    // ── Phases ───────────────────────────────────────────────────────────

    private static List<IrMethodDeclaration> CollectOverloads(IrMethodDeclaration primary)
    {
        var list = new List<IrMethodDeclaration> { primary };
        if (primary.Overloads is { Count: > 0 } others)
            list.AddRange(others);
        return list;
    }

    private static TsType ResolveCommonReturnType(
        IReadOnlyList<IrMethodDeclaration> sorted,
        bool isAsync
    )
    {
        var returns = sorted.Select(m => IrToTsTypeMapper.Map(m.ReturnType)).ToList();
        if (returns.All(t => t.Equals(returns[0])))
            return returns[0];
        return isAsync
            ? new TsNamedType("Promise", [new TsNamedType("unknown")])
            : (TsType)new TsAnyType();
    }

    private static TsMethodOverload BuildOverloadSignature(IrMethodDeclaration method) =>
        new(
            method
                .Parameters.Select(p => new TsParameter(
                    TypeScriptNaming.ToCamelCase(p.Name),
                    IrToTsTypeMapper.Map(p.Type)
                ))
                .ToList(),
            IrToTsTypeMapper.Map(method.ReturnType)
        );

    private static TsMethodMember? BuildFastPathMember(
        IrMethodDeclaration method,
        string fastPathName,
        bool isStatic,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (method.Body is null)
            return null;
        var parameters = method
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                IrToTsTypeMapper.Map(p.Type)
            ))
            .ToList();
        return new TsMethodMember(
            fastPathName,
            parameters,
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsStatementBridge.MapBody(method.Body, bclRegistry),
            Static: isStatic,
            Async: method.Semantics.IsAsync,
            // Fast paths are private — the dispatcher is the public entry point.
            Accessibility: TsAccessibility.Private,
            // Propagate method type parameters so a generic overload like
            // `Identity<T>(T value)` emits as `identityValue<T>(value: T): T`
            // instead of referencing an undeclared T.
            TypeParameters: MapTypeParameters(method.TypeParameters)
        );
    }

    private static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;
        return typeParameters
            .Select(tp => new TsTypeParameter(
                tp.Name,
                tp.Constraints is { Count: > 0 } c ? IrToTsTypeMapper.Map(c[0]) : null
            ))
            .ToList();
    }

    private static List<TsStatement> BuildDispatcherBody(
        IReadOnlyList<IrMethodDeclaration> sorted,
        IReadOnlyList<string> fastPathNames,
        string containingTypeName,
        bool isStatic
    )
    {
        var body = new List<TsStatement>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var method = sorted[i];
            var fastPathName = fastPathNames[i];
            var paramCount = method.Parameters.Count;

            TsExpression condition = new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("args"), "length"),
                "===",
                new TsLiteral(paramCount.ToString())
            );
            for (var j = 0; j < paramCount; j++)
            {
                condition = new TsBinaryExpression(
                    condition,
                    "&&",
                    IrTypeCheckBuilder.GenerateForParam(method.Parameters[j].Type, j)
                );
            }

            var callArgs = new List<TsExpression>();
            for (var j = 0; j < paramCount; j++)
            {
                callArgs.Add(
                    new TsCastExpression(
                        new TsIdentifier($"args[{j}]"),
                        IrToTsTypeMapper.Map(method.Parameters[j].Type)
                    )
                );
            }

            var receiver = isStatic
                ? (TsExpression)new TsIdentifier(containingTypeName)
                : new TsIdentifier("this");
            var delegateCall = new TsCallExpression(
                new TsPropertyAccess(receiver, fastPathName),
                callArgs
            );

            var branchStatements = new List<TsStatement>();
            if (method.ReturnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void })
            {
                branchStatements.Add(new TsExpressionStatement(delegateCall));
                branchStatements.Add(new TsReturnStatement());
            }
            else
            {
                branchStatements.Add(new TsReturnStatement(delegateCall));
            }
            body.Add(new TsIfStatement(condition, branchStatements));
        }

        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [
                        new TsStringLiteral(
                            // Legacy dispatcher emits the camelCased public name so the
                            // runtime message matches how callers spell the method.
                            $"No matching overload for {TypeScriptNaming.ToCamelCaseMember(sorted[0].Name)}"
                        ),
                    ]
                )
            )
        );
        return body;
    }

    // ── Fast-path naming ─────────────────────────────────────────────────

    /// <summary>
    /// First attempt uses parameter names (<c>addTitle</c> from <c>Add(string title)</c>);
    /// on collision (two overloads sharing parameter names is impossible in C# with
    /// different signatures, but a zero-arg overload + any single named one collide on
    /// the base name), fall back to a type-based suffix.
    /// </summary>
    private static List<string> ComputeFastPathNames(
        string baseName,
        IReadOnlyList<IrMethodDeclaration> overloads
    )
    {
        var byParamName = overloads
            .Select(m => baseName + string.Concat(m.Parameters.Select(p => Capitalize(p.Name))))
            .ToList();
        if (byParamName.Distinct(StringComparer.Ordinal).Count() == byParamName.Count)
            return byParamName;

        return overloads
            .Select(m =>
                baseName
                + string.Concat(m.Parameters.Select(p => Capitalize(SimpleTypeName(p.Type))))
            )
            .ToList();
    }

    private static string SimpleTypeName(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef p => PrimitiveSuffix(p.Primitive),
            IrArrayTypeRef a => SimpleTypeName(a.ElementType) + "Array",
            IrNullableTypeRef n => SimpleTypeName(n.Inner) + "OrNull",
            IrNamedTypeRef n => n.Name,
            _ => "Value",
        };

    /// <summary>
    /// Legacy SimpleTypeName shortens certain primitive names (Int32 → Int,
    /// System.Single → Float, etc.). Mirror that so the fallback naming matches
    /// byte-for-byte when param-name naming isn't usable.
    /// </summary>
    private static string PrimitiveSuffix(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Int32 => "Int",
            IrPrimitive.Int64 => "Long",
            IrPrimitive.String => "String",
            IrPrimitive.Boolean => "Bool",
            IrPrimitive.Float64 => "Double",
            IrPrimitive.Float32 => "Float",
            _ => primitive.ToString(),
        };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static TsAccessibility VisibilityToAccessibility(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private => TsAccessibility.Private,
            IrVisibility.Protected => TsAccessibility.Protected,
            IrVisibility.Internal => TsAccessibility.Private,
            _ => TsAccessibility.Public,
        };
}
