namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The <c>metano-runtime</c> type-check name of a 64-bit integer, per staging
/// mode (spec 012 DECIDED-22). In the staging BigInt mode the dispatcher and
/// the requirement imports use the <c>bigint</c> range checks
/// (<c>isInt64Value</c> / <c>isUInt64Value</c>, FR-048, DECIDED-14), which
/// slice 012k renames to <c>isInt64</c> / <c>isUInt64</c> when it retires the
/// shipped <c>number</c> checks; the Number mode keeps the shipped names. The
/// core's requirement scanner emits the mode-neutral names, so this is the one
/// place that spells them per mode — <see cref="IrTypeCheckBuilder.PrimitiveCheck"/>
/// and <see cref="IrRuntimeRequirementToTsImport"/> both read it.
/// </summary>
internal static class Int64TypeCheckName
{
    /// <summary>The runtime check for a <c>long</c> value in the current mode.</summary>
    public static string OfInt64 => IsBigInt ? "isInt64Value" : "isInt64";

    /// <summary>The runtime check for a <c>ulong</c> value in the current mode.</summary>
    public static string OfUInt64 => IsBigInt ? "isUInt64Value" : "isUInt64";

    private static bool IsBigInt => Int64RepresentationScope.IsBigInt;
}
