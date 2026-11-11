namespace Metano.Compiler.Extraction;

/// <summary>
/// The comparer parameter of the .NET LINQ and <c>System.MemoryExtensions</c>
/// overloads (#648, #657).
/// </summary>
public static class ComparerParameter
{
    /// <summary>
    /// The name every .NET LINQ overload and every
    /// <c>System.MemoryExtensions</c> overload gives its
    /// <c>IEqualityComparer&lt;T&gt;</c> / <c>IComparer&lt;T&gt;</c> parameter.
    /// </summary>
    public const string Name = "comparer";
}
