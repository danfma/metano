using Metano.Annotations;
using Metano.Compiler.Extraction;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The Dart target's single overload-grouping policy (spec 013 FR-039,
/// DECIDED-35): it supplies no synthesized member names in 3.0, so no
/// synthesized value-equality member joins a group and the output keeps its
/// shape.
/// </summary>
public static class DartGroupingPolicy
{
    /// <summary>The policy every Dart extraction reads.</summary>
    public static OverloadGroupingPolicy Instance { get; } =
        OverloadGroupingPolicy.For(TargetLanguage.Dart);
}
