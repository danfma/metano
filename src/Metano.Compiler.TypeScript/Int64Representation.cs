namespace Metano.Compiler.TypeScript;

/// <summary>
/// How the TypeScript target represents C#'s 64-bit integers (<c>long</c>,
/// <c>ulong</c>) while the native <c>bigint</c> mapping lands slice by slice
/// (spec 012, DECIDED-22). A compiler-internal staging switch: it has no CLI,
/// MSBuild or attribute surface, and the slice that flips the default to
/// <see cref="BigInt"/> deletes it.
/// </summary>
internal enum Int64Representation
{
    /// <summary>
    /// The shipped 2.x output: a 64-bit integer is a TS <c>number</c>, and the
    /// 64-bit conversion and overflow IR nodes render as their operand.
    /// </summary>
    Number,

    /// <summary>
    /// The 3.0 output under construction: a 64-bit integer is a native
    /// <c>bigint</c>. Only tests select it until the flip.
    /// </summary>
    BigInt,
}
