namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>Why the source generator cannot use a <c>[JsonInclude]</c> member.</summary>
internal enum JsonIncludeProblem
{
    /// <summary>The member is usable (or has no <c>[JsonInclude]</c>).</summary>
    None,

    /// <summary>
    /// A <c>private</c> / <c>protected</c> declaration or accessor, or a
    /// <c>protected internal</c> member of an assembly that does not grant
    /// access: the contract throws <c>InvalidOperationException</c>
    /// (SYSLIB1038).
    /// </summary>
    Inaccessible,

    /// <summary>
    /// A read-only field: written, and a payload that carries it throws
    /// <c>InvalidOperationException</c>.
    /// </summary>
    ReadOnlyField,

    /// <summary>
    /// An <c>internal</c> member of another assembly that does not grant
    /// <c>InternalsVisibleTo</c>: the source generator skips it.
    /// </summary>
    Skipped,
}
