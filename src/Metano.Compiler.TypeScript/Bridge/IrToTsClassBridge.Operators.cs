using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// User-defined operator lowering: single operators map to a static
/// double + an instance forwarder; overloaded operator groups route
/// through a runtime dispatcher that picks a per-overload fast-path
/// method based on argument count + type-checks.
/// </summary>
internal static partial class IrToTsClassBridge
{
    /// <summary>
    /// Lowers a single user-defined operator into a static method
    /// (<c>__add</c> / <c>__negate</c>) plus a thin instance helper
    /// (<c>$add(other)</c> / <c>$negate()</c>) that delegates to it.
    /// Multi-operator dispatch (two overloads sharing the same name) is
    /// handled by <see cref="BuildOperatorDispatcher"/>.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperator(
        IrMethodDeclaration method,
        string containingTypeName,
        string operatorName,
        IReadOnlyList<TsParameter> parameters,
        TsType returnType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry);

        var staticName = $"__{operatorName}";
        var isUnary = method.Parameters.Count == 1;
        var typeRef = new TsIdentifier(containingTypeName);

        TsStatement helperBody;
        IReadOnlyList<TsParameter> helperParams;
        if (isUnary)
        {
            helperParams = [];
            helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(typeRef, staticName),
                    [new TsIdentifier("this")]
                )
            );
        }
        else
        {
            var rightParam = parameters[^1];
            helperParams = [rightParam];
            helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(typeRef, staticName),
                    [new TsIdentifier("this"), new TsIdentifier(rightParam.Name)]
                )
            );
        }

        return
        [
            new TsMethodMember(staticName, parameters, returnType, body, Static: true),
            new TsMethodMember($"${operatorName}", helperParams, returnType, [helperBody]),
        ];
    }

    /// <summary>
    /// Lowers an overloaded operator group (two or more <c>operator +</c>
    /// methods sharing a derived name) into a static dispatcher
    /// (<c>__add(...args: unknown[])</c>) plus one private fast-path method
    /// per overload, plus a single instance helper (<c>$add(...args)</c>)
    /// that delegates to the static side with <c>this</c> as the first
    /// argument. Per-overload parameter lists, return types, and type-checks
    /// come pre-resolved from the caller because the legacy
    /// <see cref="TypeMapper"/> still owns <c>[ExportFromBcl]</c> overrides
    /// and <see cref="IrTypeCheckBuilder"/> consumes <see cref="IrTypeRef"/>
    /// directly.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperatorDispatcher(
        IReadOnlyList<IrMethodDeclaration> overloads,
        string containingTypeName,
        string operatorName,
        IReadOnlyList<IReadOnlyList<TsParameter>> overloadParameters,
        IReadOnlyList<TsType> overloadReturnTypes,
        IReadOnlyList<IReadOnlyList<IrTypeRef>> overloadParamIrTypes,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var staticName = $"__{operatorName}";
        var className = new TsIdentifier(containingTypeName);
        var members = new List<TsClassMember>();

        // Sort indices by descending arity to match the legacy "most-specific
        // first" dispatch order. The three input lists are kept parallel so
        // the index works on all of them.
        var indices = Enumerable
            .Range(0, overloads.Count)
            .OrderByDescending(i => overloads[i].Parameters.Count)
            .ToList();
        var sharedReturnType = overloadReturnTypes[indices[0]];

        var staticOverloadSigs = indices
            .Select(i => new TsMethodOverload(overloadParameters[i], overloadReturnTypes[i]))
            .ToList();

        // Fast-path naming follows the same convention as
        // IrToTsOverloadDispatcherBridge: <staticName><CapitalizedParamType>...
        var fastPathNames = indices
            .Select(i =>
                staticName
                + string.Concat(overloadParamIrTypes[i].Select(t => Capitalize(SimpleTypeName(t))))
            )
            .ToList();

        // Fast-path private static methods — one per overload, real body via
        // IrToTsStatementBridge.
        for (var k = 0; k < indices.Count; k++)
        {
            var i = indices[k];
            var body = IrToTsBodyHelpers.LowerOrNotImplemented(
                overloads[i].Body,
                overloads[i].Name,
                bclRegistry
            );
            members.Add(
                new TsMethodMember(
                    fastPathNames[k],
                    overloadParameters[i],
                    overloadReturnTypes[i],
                    body,
                    Static: true,
                    Accessibility: TsAccessibility.Private
                )
            );
        }

        // Static dispatcher body: per-overload `if (args.length === N && isT(args[i]) …)`
        // chain, throwing a runtime error when no branch matches.
        var staticDispatchBody = BuildDispatcherBranches(
            indices,
            overloadParamIrTypes,
            overloadParameters,
            fastPathNames,
            className,
            operatorName,
            paramOffset: 0,
            includeThis: false
        );

        members.Add(
            new TsMethodMember(
                staticName,
                [new TsParameter("args", new TsNamedType("unknown[]"), Rest: true)],
                sharedReturnType,
                staticDispatchBody,
                Static: true,
                Overloads: staticOverloadSigs
            )
        );

        // Instance helper signatures: drop the first parameter (it becomes
        // `this`); the dispatch ignores arity index 0 too.
        var instanceOverloadSigs = indices
            .Select(i => new TsMethodOverload(
                overloadParameters[i].Skip(1).ToList(),
                overloadReturnTypes[i]
            ))
            .ToList();

        var instanceDispatchBody = BuildDispatcherBranches(
            indices,
            overloadParamIrTypes,
            overloadParameters,
            fastPathNames,
            className,
            operatorName,
            paramOffset: 1,
            includeThis: true
        );

        members.Add(
            new TsMethodMember(
                $"${operatorName}",
                [new TsParameter("args", new TsNamedType("unknown[]"), Rest: true)],
                sharedReturnType,
                instanceDispatchBody,
                Overloads: instanceOverloadSigs
            )
        );

        return members;
    }

    /// <summary>
    /// Builds the per-branch dispatcher body shared by the static and
    /// instance helpers. <paramref name="paramOffset"/> = 0 dispatches over
    /// every overload parameter (static side); <paramref name="paramOffset"/>
    /// = 1 skips the receiver (instance side) and uses <c>this</c> as the
    /// leading call argument when <paramref name="includeThis"/> is set.
    /// </summary>
    private static List<TsStatement> BuildDispatcherBranches(
        IReadOnlyList<int> indices,
        IReadOnlyList<IReadOnlyList<IrTypeRef>> overloadParamIrTypes,
        IReadOnlyList<IReadOnlyList<TsParameter>> overloadParameters,
        IReadOnlyList<string> fastPathNames,
        TsIdentifier className,
        string operatorName,
        int paramOffset,
        bool includeThis
    )
    {
        var body = new List<TsStatement>();
        for (var k = 0; k < indices.Count; k++)
        {
            var i = indices[k];
            var paramTypes = overloadParamIrTypes[i];
            var argCount = paramTypes.Count - paramOffset;

            TsExpression condition = new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("args"), "length"),
                "===",
                new TsLiteral(argCount.ToString())
            );
            for (var j = 0; j < argCount; j++)
            {
                var check = IrTypeCheckBuilder.GenerateForParam(paramTypes[j + paramOffset], j);
                condition = new TsBinaryExpression(condition, "&&", check);
            }

            var callArgs = new List<TsExpression>();
            if (includeThis)
                callArgs.Add(new TsIdentifier("this"));
            for (var j = 0; j < argCount; j++)
            {
                // TsParameter.Type is nullable on the AST side because some
                // bridges (extension blocks, etc.) elide the annotation; in
                // the dispatcher we always have a real type so the fallback
                // is just to keep the compiler honest.
                var paramType = overloadParameters[i][j + paramOffset].Type ?? new TsAnyType();
                callArgs.Add(new TsCastExpression(new TsIdentifier($"args[{j}]"), paramType));
            }

            var delegateCall = new TsCallExpression(
                new TsPropertyAccess(className, fastPathNames[k]),
                callArgs
            );
            body.Add(new TsIfStatement(condition, [new TsReturnStatement(delegateCall)]));
        }

        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [new TsStringLiteral($"No matching overload for {operatorName}")]
                )
            )
        );
        return body;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// Short per-type tag the operator dispatcher uses in its fast-path
    /// helper names (<c>__add$Int$Int</c>, <c>__divide$Money$Decimal</c>, …),
    /// so each overload gets a distinct dispatch target.
    /// </summary>
    private static string SimpleTypeName(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef p => p.Primitive switch
            {
                IrPrimitive.Int32 => "Int",
                IrPrimitive.Int64 => "Long",
                IrPrimitive.String => "String",
                IrPrimitive.Boolean => "Bool",
                IrPrimitive.Float64 => "Double",
                IrPrimitive.Float32 => "Float",
                IrPrimitive.Decimal => "Decimal",
                _ => p.Primitive.ToString(),
            },
            IrNamedTypeRef n => n.Name,
            _ => "Unknown",
        };

    /// <summary>
    /// Maps the IR's canonical operator name (<c>"Addition"</c>,
    /// <c>"Equality"</c>, …) to the conventional TypeScript helper name
    /// (<c>"add"</c>, <c>"equals"</c>). Returns <c>null</c> for unsupported
    /// operator kinds. Unary forms (<c>"UnaryNegation"</c>, …) are folded
    /// onto the same canonical name as their binary form when there's no
    /// natural distinction at call site.
    /// </summary>
    public static string? MapOperatorKindToName(string kind) =>
        kind switch
        {
            "Addition" => "add",
            "Subtraction" => "subtract",
            "Multiply" => "multiply",
            "Division" => "divide",
            "Modulus" => "modulo",
            "Equality" => "equals",
            "Inequality" => "notEquals",
            "LessThan" => "lessThan",
            "GreaterThan" => "greaterThan",
            "LessThanOrEqual" => "lessThanOrEqual",
            "GreaterThanOrEqual" => "greaterThanOrEqual",
            "LogicalNot" => "not",
            "OnesComplement" => "bitwiseNot",
            "BitwiseAnd" => "bitwiseAnd",
            "BitwiseOr" => "bitwiseOr",
            "ExclusiveOr" => "xor",
            "LeftShift" => "shiftLeft",
            "RightShift" => "shiftRight",
            // Unary +/- collapse to the same canonical name as their binary
            // form so the call-site lowering for `-x` and `x - y` agree on
            // the helper name (matches the legacy single-token mapping).
            "UnaryNegation" => "subtract",
            "UnaryPlus" => "add",
            _ => null,
        };
}
