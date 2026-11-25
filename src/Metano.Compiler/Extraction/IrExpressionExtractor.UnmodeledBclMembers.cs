using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// BCL members whose value the lowering cannot model, reported with MS0001 instead of being
/// emitted by name (an undefined property, a missing method). A <c>DateTime</c> lowers to a
/// <c>Temporal.PlainDateTime</c>, which carries no <c>DateTimeKind</c>, so everything that reads
/// or converts by kind has no counterpart; <c>HashCode</c> accumulates through
/// <c>add(value)</c> only.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string DateTimeKindReason =
        "a DateTime lowers to a Temporal.PlainDateTime, which carries no DateTimeKind";

    private const string HashCodeReason =
        "the runtime HashCode accumulates single values through add(value) only";

    private static readonly IReadOnlyDictionary<
        (string Type, string Member),
        string
    > UnmodeledBclMembers = new Dictionary<(string, string), string>
    {
        [("System.DateTime", "Kind")] = DateTimeKindReason,
        [("System.DateTime", "ToUniversalTime")] = DateTimeKindReason,
        [("System.DateTime", "ToLocalTime")] = DateTimeKindReason,
        [("System.DateTime", "SpecifyKind")] = DateTimeKindReason,
        [("System.HashCode", "AddBytes")] = HashCodeReason,
    };

    /// <summary>
    /// Reports MS0001 at <paramref name="node"/> when <paramref name="symbol"/> is one of the
    /// <see cref="UnmodeledBclMembers"/>, or the <c>HashCode.Add(value, comparer)</c> overload.
    /// </summary>
    private static void ReportUnmodeledBclMember(SyntaxNode node, ISymbol? symbol)
    {
        if (symbol?.ContainingType is not { } owner)
            return;

        var key = (owner.ToDisplayString(), symbol.Name);

        if (UnmodeledBclMembers.TryGetValue(key, out var reason))
            ReportUnsupportedFeature(
                node,
                $"'{owner.Name}.{symbol.Name}' is not supported: {reason}."
            );
        else if (IsHashCodeAddWithComparer(owner, symbol))
            ReportUnsupportedFeature(
                node,
                $"'HashCode.Add' with an IEqualityComparer is not supported: {HashCodeReason}."
            );
    }

    private static bool IsHashCodeAddWithComparer(INamedTypeSymbol owner, ISymbol symbol) =>
        owner.ToDisplayString() == "System.HashCode"
        && symbol is IMethodSymbol { Name: "Add", Parameters.Length: 2 };
}
