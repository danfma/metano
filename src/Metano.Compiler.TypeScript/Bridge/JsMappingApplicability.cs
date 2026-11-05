using Metano.Compiler.Mappings;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Which declarative mapping entries the TypeScript target applies in the
/// running transform: an entry with JavaScript data, unless it is a
/// <c>bigint</c> template of spec 012's staging
/// (<see cref="DeclarativeMappingEntry.WhenInt64IsBigInt"/>) and 64-bit
/// integers are still <c>number</c> values.
/// </summary>
internal static class JsMappingApplicability
{
    public static bool IsActiveJsMapping(this DeclarativeMappingEntry entry) =>
        // STAGING(012k): every JS entry applies once the switch is gone.
        entry.HasJsMapping && (!entry.WhenInt64IsBigInt || Int64RepresentationScope.IsBigInt);
}
