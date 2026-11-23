using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>MS0037</c> warnings of the <c>nint</c> / <c>nuint</c> slots a
/// context reaches (#561): System.Text.Json has no converter for
/// <c>IntPtr</c> / <c>UIntPtr</c> (measured on .NET 10 and 11: every non-null
/// use throws <c>NotSupportedException</c>, and its source generator does not
/// warn), while the TypeScript context reads and writes them as 64-bit
/// integers (spec 012 DECIDED-2, #529). A TypeScript client can then send a
/// payload the .NET side cannot read. One warning per serialized member
/// (generic constructions share their definition's) and per value root whose
/// descriptor nests a native integer.
/// </summary>
internal sealed class JsonNativeIntegerWarnings(JsonPropertyCollector properties)
{
    private const string Consequence =
        "System.Text.Json has no converter for IntPtr / UIntPtr and throws "
        + "NotSupportedException, while the TypeScript context reads and writes it as a "
        + "64-bit integer.";

    private readonly JsonPropertyCollector _properties = properties;

    public IEnumerable<MetanoDiagnostic> WarningsOf(IReadOnlyList<JsonContractEntry> entries)
    {
        var rootWarnings = entries
            .Where(entry => entry is { Kind: not JsonPropertyKind.Ref, Root: not null })
            .Where(entry => ReachesNativeInteger(entry.Type))
            .Select(entry => RootWarningOf(entry.Root!));

        var memberWarnings = entries
            .Where(entry => entry.Kind == JsonPropertyKind.Ref)
            .Select(entry => entry.Type)
            .OfType<INamedTypeSymbol>()
            .SelectMany(_properties.DeclarationOrderPropertiesOf)
            .Where(property => ReachesNativeInteger(property.Type))
            .Select(property => property.Member.OriginalDefinition)
            .Distinct(SymbolEqualityComparer.Default)
            .Select(MemberWarningOf);

        return rootWarnings.Concat(memberWarnings);
    }

    private static MetanoDiagnostic RootWarningOf(JsonContractRoot root) =>
        UnmirroredJsonWarning.Create(
            $"The [JsonSerializable] root '{UnmirroredJsonWarning.TypeName(root.Type)}' holds "
                + $"a nint or nuint: {Consequence}",
            root.Location
        );

    private static MetanoDiagnostic MemberWarningOf(ISymbol member) =>
        UnmirroredJsonWarning.Create(
            $"'{UnmirroredJsonWarning.MemberName(member)}' holds a nint or nuint: {Consequence}",
            UnmirroredJsonWarning.At(member)
        );

    /// <summary>
    /// Whether <paramref name="type"/> is a native integer or nests one through
    /// the container shapes its descriptor nests.
    /// </summary>
    private static bool ReachesNativeInteger(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr
        || JsonTypeClassifier
            .ChildTypesOf(type, JsonTypeClassifier.Classify(type))
            .Any(ReachesNativeInteger);
}
