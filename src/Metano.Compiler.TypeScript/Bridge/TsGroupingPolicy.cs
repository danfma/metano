using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript target's single overload-grouping policy (spec 013 FR-039):
/// the synthesized value-equality members keep the names the runtime calls —
/// <c>valueEquals</c>, <c>HashSet</c> and <c>Dictionary</c> call
/// <c>equals(other)</c> and <c>hashCode()</c>, a <c>with</c> expression
/// <c>with(overrides)</c> — and, when they join a user group, take the pinned
/// <c>ByValue</c> fast path (<c>equalsByValue</c>, DECIDED-34).
/// </summary>
public static class TsGroupingPolicy
{
    /// <summary>The fast-path suffix of a synthesized member that joins a group.</summary>
    public const string FastPathSuffix = "ByValue";

    /// <summary>The policy every TypeScript extraction reads.</summary>
    public static OverloadGroupingPolicy Instance { get; } =
        new(
            TargetLanguage.TypeScript,
            new SynthesizedMemberNames(
                IrToTsRecordSynthesisBridge.EqualsMethodName,
                IrToTsNamingPolicy.HashCodeMethodName,
                IrToTsRecordSynthesisBridge.WithMethodName,
                FastPathSuffix,
                groupsEquals: true
            )
        );

    /// <summary>
    /// The name <paramref name="kind"/> is emitted under: the one map from a
    /// synthesized member to its TypeScript name.
    /// </summary>
    public static string NameOf(IrSynthesizedValueMember kind) =>
        Instance.NameOf(kind)
        ?? throw new InvalidOperationException(
            $"TypeScript names every synthesized member; {kind} has none."
        );
}
