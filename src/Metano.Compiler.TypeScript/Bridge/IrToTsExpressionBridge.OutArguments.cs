using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Calls with <c>out</c> arguments (<see cref="IrOutArgumentCall"/>). The
/// callee returns <c>[result, out1, …]</c> (see
/// <see cref="OutParameterLowering"/>), so the call itself is the invocation
/// without its <c>out</c> arguments. A declaratively mapped BCL method keeps
/// one argument slot per <c>out</c> parameter, filled with C#'s
/// <c>default(T)</c> for it, so a template such as
/// <c>tryGetValue($this, $0, $1)</c> receives the value to report on a miss.
/// The mapping of a method with <c>out</c> parameters must therefore be a
/// template that yields <c>[result, out…]</c>; a plain <c>JsMethod</c> rename
/// would not.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// The invocation whose value is the <c>[result, out1, …]</c> tuple — the
    /// initializer of the destructuring that binds it.
    /// </summary>
    public static TsExpression MapOutArgumentCallValue(
        IrOutArgumentCall outCall,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (HasJsMapping(outCall, bclRegistry))
            return Map(WithOutDefaults(outCall), bclRegistry);

        if (outCall.IsExternalCallee)
            ReportUnmappedExternalCallee(outCall.Call);

        return Map(outCall.Call, bclRegistry);
    }

    /// <summary>
    /// A BCL or third-party method with <c>out</c> parameters and no
    /// declarative mapping: nothing makes it return <c>[result, out…]</c>, so
    /// the emitted call has no such result to unpack.
    /// </summary>
    private static void ReportUnmappedExternalCallee(IrCallExpression call) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{call.Origin?.DeclaringTypeFullName}.{call.Origin?.MemberName}' takes 'out' "
                    + "arguments but has no declarative mapping returning its 'out' values: "
                    + "the call is emitted without them."
            )
        );

    /// <summary>
    /// A call left in expression position (a short-circuit operand, a
    /// conditional branch): assigns the <c>out</c> values to their targets and
    /// yields the result, <c>([, v] = call)[0]</c>. The value of a
    /// destructuring assignment is its right-hand side, so the call runs once
    /// and no temporary is needed.
    /// </summary>
    private static TsExpression MapOutArgumentCall(
        IrOutArgumentCall outCall,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var value = MapOutArgumentCallValue(outCall, bclRegistry);

        if (outCall.OutArguments.All(o => o.Target is IrOutDiscard))
            return value;

        var targets = outCall.OutArguments.Select(o => MapOutTarget(o.Target, bclRegistry));
        var pattern = outCall.ResultType is null ? targets : targets.Prepend(new TsArrayHole());
        var assignment = new TsBinaryExpression(new TsArrayLiteral(pattern.ToList()), "=", value);

        return outCall.ResultType is null
            ? assignment
            : new TsElementAccess(new TsParenthesized(assignment), new TsLiteral("0"));
    }

    private static TsExpression MapOutTarget(
        IrOutTarget target,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        target switch
        {
            IrOutLocalDeclaration local => new TsIdentifier(local.Name),
            IrOutAssignment assignment => Map(assignment.Destination, bclRegistry),
            _ => new TsArrayHole(),
        };

    /// <summary>
    /// Whether a declarative JS mapping exists for the call with its <c>out</c>
    /// slots back in place — by name and, when the entry filters on it, by
    /// argument count, so an unmapped overload (<c>int.TryParse(s, styles,
    /// provider, out n)</c>) is not mistaken for the mapped one.
    /// </summary>
    private static bool HasJsMapping(
        IrOutArgumentCall outCall,
        DeclarativeMappingRegistry? registry
    )
    {
        var arity = outCall.Call.Arguments.Count + outCall.OutArguments.Count;

        return registry is not null
            && outCall.Call.Origin is { } origin
            && registry.TryGetMethodsByFullName(
                origin.DeclaringTypeFullName,
                origin.MemberName,
                out var candidates
            )
            && candidates.Any(candidate =>
                candidate.HasJsMapping && candidate.MatchesOverload(arity, origin.ParameterTypes)
            );
    }

    /// <summary>
    /// The call with each <c>out</c> argument's <c>default(T)</c> back at its
    /// parameter position. Inserting in order relies on
    /// <see cref="IrOutArgumentCall.OutArguments"/> being sorted by ordinal;
    /// the position is clamped to the argument count as a safeguard.
    /// </summary>
    private static IrCallExpression WithOutDefaults(IrOutArgumentCall outCall)
    {
        var arguments = outCall.Call.Arguments.ToList();

        foreach (var outArgument in outCall.OutArguments)
        {
            var position = Math.Min(outArgument.ParameterOrdinal, arguments.Count);

            arguments.Insert(position, new IrArgument(outArgument.DefaultValue));
        }

        return outCall.Call with
        {
            Arguments = arguments,
        };
    }

    /// <summary>
    /// <c>[found, value] as readonly [found: true, …] | readonly [found: false, …]</c>:
    /// the only cast the TypeScript output keeps (see <see cref="OutParameterLowering"/>).
    /// </summary>
    private static TsExpression MapOutResultAssertion(
        IrCastExpression cast,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new TsCastExpression(
            Map(cast.Expression, bclRegistry),
            IrToTsTypeMapper.Map(cast.TargetType)
        );
}
