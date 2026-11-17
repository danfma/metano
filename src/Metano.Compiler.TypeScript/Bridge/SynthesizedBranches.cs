using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The dispatcher branches of a synthesized value-equality member (spec 013
/// FR-042). The first runs before every checked branch, for the calls the
/// runtime makes: <c>equals</c> with an argument of the receiver's own runtime
/// type, <c>hashCode()</c>, <c>with</c> with a plain overrides object or none.
/// The closing one runs after the checked branches, for any other call at the
/// member's argument counts, so a foreign argument reaches <c>equals</c> and
/// answers <c>false</c> instead of throwing.
/// </summary>
internal static class SynthesizedBranches
{
    /// <summary>The dispatcher's first argument, <c>args[0]</c>.</summary>
    private static TsExpression Argument => OverloadArgumentShape.ArgumentAt(0);

    /// <summary>The branch tried before every checked branch.</summary>
    public static TsStatement First(
        IrMethodDeclaration synthesized,
        TsExpression receiver,
        string fastPath,
        IrToTsTypeOverrides? typeOverrides
    ) => Branch(FirstGuardOf(synthesized), synthesized, receiver, fastPath, typeOverrides);

    /// <summary>
    /// The branch tried after the checked branches; <c>null</c> for
    /// <c>hashCode()</c>, whose first branch already takes every call it accepts.
    /// </summary>
    public static TsStatement? Closing(
        IrMethodDeclaration synthesized,
        TsExpression receiver,
        string fastPath,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        synthesized.SynthesizedKind is IrSynthesizedValueMember.HashCode
            ? null
            : Branch(CountGuardOf(synthesized), synthesized, receiver, fastPath, typeOverrides);

    private static TsExpression FirstGuardOf(IrMethodDeclaration synthesized) =>
        synthesized.SynthesizedKind switch
        {
            IrSynthesizedValueMember.Equals => All(
                CountIs("===", 1),
                new TsBinaryExpression(Argument, "!=", new TsLiteral("null")),
                new TsBinaryExpression(
                    PrototypeOf(Argument),
                    "===",
                    PrototypeOf(new TsIdentifier("this"))
                )
            ),
            IrSynthesizedValueMember.Copy => new TsBinaryExpression(
                CountIs("===", 0),
                "||",
                new TsParenthesized(
                    All(
                        CountIs("===", 1),
                        new TsParenthesized(
                            new TsBinaryExpression(
                                OverloadArgumentShape.IsOmitted(Argument),
                                "||",
                                new TsBinaryExpression(
                                    PrototypeOf(Argument),
                                    "===",
                                    new TsPropertyAccess(new TsIdentifier("Object"), "prototype")
                                )
                            )
                        )
                    )
                )
            ),
            _ => CountIs("===", 0),
        };

    private static TsExpression CountGuardOf(IrMethodDeclaration synthesized) =>
        synthesized.SynthesizedKind is IrSynthesizedValueMember.Copy
            ? CountIs("<=", 1)
            : CountIs("===", 1);

    private static TsStatement Branch(
        TsExpression guard,
        IrMethodDeclaration synthesized,
        TsExpression receiver,
        string fastPath,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new TsIfStatement(
            guard,
            [
                new TsReturnStatement(
                    new TsCallExpression(
                        new TsPropertyAccess(receiver, fastPath),
                        ArgumentsOf(synthesized, typeOverrides)
                    )
                ),
            ]
        );

    /// <summary>
    /// <c>args[0]</c> for <c>equals</c>, cast to the copy's overrides type for
    /// <c>with</c>; nothing for <c>hashCode()</c>.
    /// </summary>
    private static IReadOnlyList<TsExpression> ArgumentsOf(
        IrMethodDeclaration synthesized,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        synthesized.SynthesizedKind switch
        {
            IrSynthesizedValueMember.Equals =>
            [
                EqualsArgument(synthesized.Parameters[0], typeOverrides),
            ],
            IrSynthesizedValueMember.Copy =>
            [
                new TsCastExpression(
                    Argument,
                    IrToTsRecordSynthesisBridge.OverridesTypeOf(
                        IrToTsTypeMapper.Map(synthesized.ReturnType, typeOverrides)
                    )
                ),
            ],
            _ => [],
        };

    /// <summary>
    /// <c>args[0]</c>, cast to the parameter of a user <c>Equals(object)</c>
    /// folded into the member when the target maps it to a narrower type than
    /// <c>unknown</c>.
    /// </summary>
    private static TsExpression EqualsArgument(
        IrParameter parameter,
        IrToTsTypeOverrides? typeOverrides
    )
    {
        var type = IrToTsTypeMapper.MapParameterType(parameter, typeOverrides);

        return type is TsAnyType or TsNamedType { Name: "unknown" }
            ? Argument
            : new TsCastExpression(Argument, type);
    }

    private static TsExpression CountIs(string comparison, int count) =>
        new TsBinaryExpression(
            new TsPropertyAccess(new TsIdentifier("args"), "length"),
            comparison,
            new TsLiteral(count.ToString())
        );

    private static TsExpression All(params TsExpression[] clauses) =>
        clauses.Aggregate(
            (accumulated, clause) => new TsBinaryExpression(accumulated, "&&", clause)
        );

    private static TsExpression PrototypeOf(TsExpression value) =>
        new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Object"), "getPrototypeOf"),
            [value]
        );
}
