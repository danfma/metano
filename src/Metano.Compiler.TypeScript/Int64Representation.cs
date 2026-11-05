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

/// <summary>
/// The <see cref="Int64Representation"/> of the transform running on this
/// execution context, read by the static type mapper and expression bridge.
/// <see cref="Transformation.TypeTransformer"/> publishes it for the duration
/// of a transform; an <see cref="AsyncLocal{T}"/> keeps parallel transforms
/// (and parallel tests) apart. Removed with the staging switch (012k).
/// </summary>
internal static class Int64RepresentationScope
{
    private static readonly AsyncLocal<Int64Representation> _current = new();

    public static Int64Representation Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>Whether 64-bit integers are native <c>bigint</c> values.</summary>
    public static bool IsBigInt => Current is Int64Representation.BigInt;
}
