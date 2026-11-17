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
    /// (<c>__add</c> / <c>__negate</c>) plus, when the operator applies to its
    /// declaring type as its left (or only) operand, a thin instance helper
    /// (<c>$add(other)</c> / <c>$negate()</c>) that delegates to it; an
    /// operator declared for its right operand (<c>operator +(int a, Meters m)</c>)
    /// has no instance form (baseline FR-078). Multi-operator dispatch (two
    /// overloads sharing the same name) is handled by
    /// <see cref="BuildOperatorDispatcher"/>.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperator(
        IrMethodDeclaration method,
        TsOperatorOwner owner,
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

        var staticName = TsOperatorNaming.StaticHelperName(operatorName);

        var helper = new TsMethodMember(
            staticName,
            parameters,
            returnType,
            body,
            Static: true,
            TypeParameters: owner.TypeParameters
        );

        if (!method.Semantics.TakesDeclaringTypeFirst)
            return [helper];

        return [helper, InstanceForwarder(owner, operatorName, parameters, returnType)];
    }

    /// <summary>
    /// The instance form of a single operator: the receiver is its first
    /// operand (<c>$add(b) { return T.__add(this, b); }</c>).
    /// </summary>
    private static TsMethodMember InstanceForwarder(
        TsOperatorOwner owner,
        string operatorName,
        IReadOnlyList<TsParameter> parameters,
        TsType returnType
    )
    {
        var forwarded = parameters.Skip(1).ToList();

        var call = new TsCallExpression(
            new TsPropertyAccess(
                new TsIdentifier(owner.TypeName),
                TsOperatorNaming.StaticHelperName(operatorName)
            ),
            [new TsIdentifier("this"), .. forwarded.Select(p => new TsIdentifier(p.Name))]
        );

        return new TsMethodMember(
            TsOperatorNaming.InstanceHelperName(operatorName),
            forwarded,
            returnType,
            [new TsReturnStatement(call)]
        );
    }

    /// <summary>
    /// Lowers a user-defined conversion operator into its own static
    /// (<c>static __toInt32(m: Money): number</c>): conversions never share a
    /// dispatcher, since C# selects one by its source and target types and
    /// never by its arguments, and they declare no instance form (baseline
    /// FR-078, #600).
    /// </summary>
    public static TsMethodMember BuildConversion(
        IrMethodDeclaration conversion,
        TsOperatorOwner owner,
        IReadOnlyList<TsParameter> parameters,
        TsType returnType,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new(
            TsOperatorNaming.ConversionHelperName(conversion.Semantics.ConversionName!),
            parameters,
            returnType,
            IrToTsBodyHelpers.LowerOrNotImplemented(
                conversion.Body,
                conversion.Name,
                IrToTsBridgeContext.For(bclRegistry)
            ),
            Static: true,
            TypeParameters: owner.TypeParameters
        );

    /// <summary>
    /// Whether an operator, or any overload of its group, applies to its
    /// declaring type as its left operand, so the type declares its instance
    /// form (<c>$add</c>).
    /// </summary>
    public static bool HasInstanceForm(IrMethodDeclaration op) =>
        new[] { op }
            .Concat(op.Overloads ?? [])
            .Any(overload => overload.Semantics.TakesDeclaringTypeFirst);

    /// <summary>
    /// Lowers an overloaded operator group (two or more <c>operator +</c>
    /// methods sharing a derived name) into a static dispatcher
    /// (<c>__add(...args: unknown[])</c>) plus one public fast-path method
    /// per overload (<c>__addMoneyInt32</c>), plus a single instance helper (<c>$add(...args)</c>)
    /// that delegates to the static side with <c>this</c> as the first
    /// argument, over the overloads whose first operand is the declaring type
    /// (none when no overload has one, baseline FR-078). The statics of a
    /// generic type re-declare its type parameters. Per-overload parameter lists, return types, and type-checks
    /// come pre-resolved from the caller because the legacy
    /// <see cref="TypeMapper"/> still owns <c>[ExportFromBcl]</c> overrides
    /// and <see cref="IrTypeCheckBuilder"/> consumes <see cref="IrTypeRef"/>
    /// directly. The branches follow <see cref="OverloadDispatchOrder"/>, and a
    /// type-parameter operand resolves against
    /// <paramref name="typeCheckScope"/>.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperatorDispatcher(
        IReadOnlyList<IrMethodDeclaration> overloads,
        TsOperatorOwner owner,
        string operatorName,
        IReadOnlyList<IReadOnlyList<TsParameter>> overloadParameters,
        IReadOnlyList<TsType> overloadReturnTypes,
        IReadOnlyList<IReadOnlyList<IrTypeRef>> overloadParamIrTypes,
        TypeCheckScope typeCheckScope,
        Func<IrMethodDeclaration, string> fastPathNameOf,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var staticName = TsOperatorNaming.StaticHelperName(operatorName);
        var className = new TsIdentifier(owner.TypeName);
        var members = new List<TsClassMember>();

        // The three input lists are kept parallel, so the sorted index works
        // on all of them.
        var indices = OverloadDispatchOrder.Sort(Enumerable.Range(0, overloads.Count), i =>
            overloads[i].Parameters
        );

        // Operator overloads can differ in return type (`int` and `long`
        // conversions, `M + int` beside `M + string`), so the implementation
        // signature follows the dispatcher rule: theirs when they agree, else
        // the erased one. An operator is neither generic nor async.
        var sharedReturnType = IrToTsOverloadDispatcherBridge.CommonReturnTypeOf(
            overloadReturnTypes,
            isGeneric: false,
            isAsync: false
        );

        var staticOverloadSigs = indices
            .Select(i => new TsMethodOverload(
                overloadParameters[i],
                overloadReturnTypes[i],
                owner.TypeParameters
            ))
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
                    Static: true,
                    TypeParameters: owner.TypeParameters
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
                TypeParameters: owner.TypeParameters,
                Overloads: staticOverloadSigs
            )
        );

        var receiverPositions = Enumerable
            .Range(0, indices.Count)
            .Where(k => overloads[indices[k]].Semantics.TakesDeclaringTypeFirst)
            .ToList();

        if (receiverPositions.Count == 0)
            return members;

        var receiverIndices = receiverPositions.Select(k => indices[k]).ToList();
        var receiverFastPaths = receiverPositions.Select(k => fastPathNames[k]).ToList();

        // Instance helper signatures: drop the first parameter (it becomes
        // `this`); the dispatch ignores arity index 0 too.
        var instanceOverloadSigs = receiverIndices
            .Select(i => new TsMethodOverload(
                overloadParameters[i].Skip(1).ToList(),
                overloadReturnTypes[i]
            ))
            .ToList();

        var instanceDispatchBody = BuildDispatcherBranches(
            receiverIndices,
            overloadParamIrTypes,
            overloadParameters,
            receiverFastPaths,
            className,
            operatorName,
            typeCheckScope,
            paramOffset: 1,
            includeThis: true
        );

        members.Add(
            new TsMethodMember(
                TsOperatorNaming.InstanceHelperName(operatorName),
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
}
