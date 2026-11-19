using System.Globalization;
using System.Text.RegularExpressions;

namespace Metano.Compiler.Mappings;

/// <summary>
/// The <c>$R&lt;n&gt;</c> placeholder of a declarative mapping's template: the
/// runtime type descriptor of the call's method type argument <c>n</c>
/// (<c>enumFromRaw($R0, $0)</c>, spec 011 FR-024). The one definition of its
/// grammar: the frontend reads which descriptors a template asks for, and a
/// target's template printer substitutes them.
/// </summary>
public static partial class TypeArgumentDescriptorPlaceholder
{
    /// <summary>The text before the type argument's index.</summary>
    public const string Prefix = "$R";

    /// <summary>The type argument indices whose descriptor <paramref name="template"/> reads.</summary>
    public static IReadOnlySet<int> IndicesIn(string template) =>
        Pattern()
            .Matches(template)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

    [GeneratedRegex(@"\$R(\d+)")]
    private static partial Regex Pattern();
}
