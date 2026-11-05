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
        // Operators are static: no class scope, so no backing fields exist.
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(
            method.Body,
            method.Name,
            IrToTsBridgeContext.For(bclRegistry)
        );

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
    /// (<c>__add(...args: unknown[])</c>) plus one public fast-path method
    /// per overload (<c>__addMoneyInt32</c>), plus a single instance helper (<c>$add(...args)</c>)
    /// that delegates to the static side with <c>this</c> as the first
    /// argument. Per-overload parameter lists, return types, and type-checks
    /// come pre-resolved from the caller because the legacy
    /// <see cref="TypeMapper"/> still owns <c>[ExportFromBcl]</c> overrides
    /// and <see cref="IrTypeCheckBuilder"/> consumes <see cref="IrTypeRef"/>
    /// directly. The branches follow <see cref="OverloadDispatchOrder"/>, and a
    /// type-parameter operand resolves against
    /// <paramref name="typeCheckScope"/>.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperatorDispatcher(
        IReadOnlyList<IrMethodDeclaration> overloads,
        string containingTypeName,
        string operatorName,
        IReadOnlyList<IReadOnlyList<TsParameter>> overloadParameters,
        IReadOnlyList<TsType> overloadReturnTypes,
        IReadOnlyList<IReadOnlyList<IrTypeRef>> overloadParamIrTypes,
        TypeCheckScope typeCheckScope,
        Func<IrMethodDeclaration, string> fastPathNameOf,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var staticName = $"__{operatorName}";
        var className = new TsIdentifier(containingTypeName);
        var members = new List<TsClassMember>();

        // The three input lists are kept parallel, so the sorted index works
        // on all of them.
        var indices = OverloadDispatchOrder.Sort(
            Enumerable.Range(0, overloads.Count),
            i => overloads[i].Parameters
        );
        var sharedReturnType = overloadReturnTypes[indices[0]];

        var staticOverloadSigs = indices
            .Select(i => new TsMethodOverload(overloadParameters[i], overloadReturnTypes[i]))
            .ToList();

        // The core's naming policy spells operator overloads by their parameter
        // types: <staticName><Suffix> (__addMoneyInt32, spec 013 FR-033), and
        // the class's claim pass resolves the name.
        var fastPathNames = indices.Select(i => fastPathNameOf(overloads[i])).ToList();

        // Fast-path static methods — one per overload, real body via
        // IrToTsStatementBridge. Public, unlike method fast paths, so resolved
        // operator call sites can target them directly (spec 013 FR-033).
        for (var k = 0; k < indices.Count; k++)
        {
            var i = indices[k];
            var body = IrToTsBodyHelpers.LowerOrNotImplemented(
                overloads[i].Body,
                overloads[i].Name,
                IrToTsBridgeContext.For(bclRegistry)
            );
            members.Add(
                new TsMethodMember(
                    fastPathNames[k],
                    overloadParameters[i],
                    overloadReturnTypes[i],
                    body,
                    Static: true
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
            typeCheckScope,
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
            typeCheckScope,
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
        TypeCheckScope typeCheckScope,
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
                var check = IrTypeCheckBuilder.Generate(
                    paramTypes[j + paramOffset],
                    OverloadArgumentShape.ArgumentAt(j),
                    typeCheckScope
                );

                if (check is not null)
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
                callArgs.Add(new TsCastExpression(OverloadArgumentShape.ArgumentAt(j), paramType));
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
