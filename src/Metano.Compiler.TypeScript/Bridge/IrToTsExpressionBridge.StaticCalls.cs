using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

public static partial class IrToTsExpressionBridge
{
    private static bool NeedsStaticCallGuard(IrBoundStaticCall? call) =>
        call
            is {
                IsContainerless: false,
                HasExtensionMembers: false,
                DeclaringType.Semantics:
                {
                    IsTranspilable: true,
                    IsIgnored: false,
                    IsExternal: false,
                    IsPlainObject: false,
                    IsJsTuple: false,
                    Kind: IrNamedTypeKind.Class
                        or IrNamedTypeKind.Record
                        or IrNamedTypeKind.Struct
                        or IrNamedTypeKind.Exception,
                },
            };

    private static TsOrderedArguments BindStaticCallArguments(
        IReadOnlyList<TsExpression> arguments,
        IReadOnlyList<int>? order
    ) =>
        TsEvaluationOrder.Bind(
            arguments,
            order ?? Enumerable.Range(0, arguments.Count).ToList(),
            BoundArgumentPrefix
        );

    private static TsExpression GuardStaticCall(
        IrBoundStaticCall call,
        TsOrderedArguments arguments,
        TsExpression result
    )
    {
        var guard = new TsExpressionStatement(
            new TsCallExpression(
                new TsPropertyAccess(CanonicalTypeValue(call.DeclaringType), "$initialize"),
                []
            )
        );

        var trigger = call.IsLifted
            ? (TsStatement)
                new TsIfStatement(
                    new TsBinaryExpression(
                        new TsBinaryExpression(arguments.Arguments[0], "!=", new TsLiteral("null")),
                        "&&",
                        new TsBinaryExpression(arguments.Arguments[1], "!=", new TsLiteral("null"))
                    ),
                    [guard]
                )
            : guard;

        return TsImmediateInvocation.Of(
            arguments.Bindings,
            [trigger, new TsReturnStatement(result)],
            isAsync: false
        );
    }

    private static TsExpression MapStaticEquality(
        IrBinaryExpression binary,
        IrToTsBridgeContext context
    )
    {
        var arguments = BindStaticCallArguments(
            [Map(binary.Left, context), Map(binary.Right, context)],
            null
        );

        var bound = binary with
        {
            Left = BoundOperand(binary.Left, 0),
            Right = BoundOperand(binary.Right, 1),
            BoundStaticCall = null,
        };

        return GuardStaticCall(binary.BoundStaticCall!, arguments, MapBinary(bound, context));
    }

    // A null literal stays in the comparison: the loose `== null` decision reads it.
    private static IrExpression BoundOperand(IrExpression operand, int index) =>
        operand is IrLiteral { Kind: IrLiteralKind.Null }
            ? operand
            : new IrIdentifier(BoundArgumentPrefix + index);
}
