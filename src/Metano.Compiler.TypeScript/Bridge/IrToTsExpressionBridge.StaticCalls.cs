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
        IReadOnlyList<int>? order,
        IrToTsBridgeContext context
    ) =>
        TsEvaluationOrder.Bind(
            arguments,
            order ?? Enumerable.Range(0, arguments.Count).ToList(),
            BoundArgumentPrefix,
            context
        );

    /// <summary>
    /// A call to a static member of a lazily initialized type: the arguments,
    /// in C#'s order, then the type's initialization, then the call —
    /// <c>($arg0 = a, T.$initialize(), T.f($arg0))</c> (<see cref="BoundBefore"/>).
    /// A lifted operator initializes the type only when both operands are
    /// non-null, as C# skips the call then.
    /// </summary>
    private static TsExpression GuardStaticCall(
        IrBoundStaticCall call,
        TsOrderedArguments arguments,
        TsExpression result,
        IrToTsBridgeContext context
    )
    {
        var guard = new TsCallExpression(
            new TsPropertyAccess(CanonicalTypeValue(call.DeclaringType), "$initialize"),
            []
        );

        var trigger = call.IsLifted
            ? new TsBinaryExpression(
                new TsBinaryExpression(
                    new TsBinaryExpression(arguments.Arguments[0], "!=", new TsLiteral("null")),
                    "&&",
                    new TsBinaryExpression(arguments.Arguments[1], "!=", new TsLiteral("null"))
                ),
                "&&",
                guard
            )
            : (TsExpression)guard;

        return BoundBefore(arguments.Bindings, result, context, effects: [trigger]);
    }

    private static TsExpression MapStaticEquality(
        IrBinaryExpression binary,
        IrToTsBridgeContext context
    )
    {
        var arguments = BindStaticCallArguments(
            [Map(binary.Left, context), Map(binary.Right, context)],
            null,
            context
        );

        var bound = binary with
        {
            Left = BoundOperand(binary.Left, 0),
            Right = BoundOperand(binary.Right, 1),
            BoundStaticCall = null,
        };

        var boundContext = context with
        {
            BoundElements = context
                .BoundElements.SetItem(BoundArgumentPrefix + 0, arguments.Arguments[0])
                .SetItem(BoundArgumentPrefix + 1, arguments.Arguments[1]),
        };

        return GuardStaticCall(
            binary.BoundStaticCall!,
            arguments,
            MapBinary(bound, boundContext),
            context
        );
    }

    // A null literal stays in the comparison: the loose `== null` decision reads it.
    private static IrExpression BoundOperand(IrExpression operand, int index) =>
        operand is IrLiteral { Kind: IrLiteralKind.Null }
            ? operand
            : new IrIdentifier(BoundArgumentPrefix + index);
}
