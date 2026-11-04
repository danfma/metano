namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>How a read sets a serialized member that no constructor parameter binds.</summary>
internal enum JsonReadAssignment
{
    /// <summary>Never: a get-only property or a read-only field (written only).</summary>
    None,

    /// <summary>
    /// Always, through the object initializer: an <c>init</c> accessor or a
    /// <c>required</c> member; a missing key sets the default.
    /// </summary>
    Always,

    /// <summary>
    /// Only when the payload carries the key: a <c>set</c> accessor or a
    /// writable field; a missing key keeps the constructed value.
    /// </summary>
    IfPresent,
}
