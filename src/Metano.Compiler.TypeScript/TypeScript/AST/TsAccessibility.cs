namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// The access modifier of a class member, constructor or parameter property.
/// <see cref="None"/> marks a constructor parameter that is not a parameter
/// property.
/// </summary>
public enum TsAccessibility
{
    Public,
    Protected,
    Private,
    None,
}

/// <summary>The TypeScript keyword of a <see cref="TsAccessibility"/>.</summary>
public static class TsAccessibilityKeywords
{
    /// <summary>
    /// <c>public</c>, <c>protected</c> or <c>private</c>; empty for
    /// <see cref="TsAccessibility.None"/>.
    /// </summary>
    public static string ToKeyword(this TsAccessibility accessibility) =>
        accessibility switch
        {
            TsAccessibility.Public => "public",
            TsAccessibility.Protected => "protected",
            TsAccessibility.Private => "private",
            _ => string.Empty,
        };
}
