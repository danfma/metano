namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether the runtime pipe operator of a LINQ stage implements the overload
/// C# bound (#648).
/// </summary>
public enum IrLinqOverloadSupport
{
    /// <summary>The runtime operator implements the overload.</summary>
    Implemented,

    /// <summary>
    /// The runtime operator implements the overload without its comparer, so
    /// the comparer would be ignored and the default comparison used.
    /// </summary>
    IgnoredComparer,

    /// <summary>The runtime operator implements no such overload.</summary>
    Unimplemented,
}
