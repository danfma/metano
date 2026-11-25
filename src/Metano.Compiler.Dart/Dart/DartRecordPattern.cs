using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart;

/// <summary>
/// The Dart 3 record pattern of a deconstruction whose value is a C# tuple
/// (a Dart record): <c>var (a, _, (b, c)) = value;</c>. Any other shape — a
/// part read by member, a value whose type is not a tuple — has no pattern
/// here and stays unsupported.
/// </summary>
internal static class DartRecordPattern
{
    /// <summary>The pattern of <paramref name="deconstruction"/>, or <c>null</c> when it has none.</summary>
    public static string? Of(IrTupleDeconstruction deconstruction) =>
        deconstruction.Members is null
            ? PatternOf(deconstruction.Elements, deconstruction.ValueType)
            : null;

    private static string? PatternOf(
        IReadOnlyList<IrDeconstructionElement> elements,
        IrTypeRef? valueType
    )
    {
        if (valueType is not IrTupleTypeRef tuple || tuple.Elements.Count != elements.Count)
            return null;

        var parts = elements.Select((element, index) => PartOf(element, tuple.Elements[index]));
        var printed = parts.ToList();

        return printed.Any(part => part is null) ? null : $"({string.Join(", ", printed)})";
    }

    private static string? PartOf(IrDeconstructionElement element, IrTypeRef partType) =>
        element switch
        {
            { Nested: { Members: null } nested } => PatternOf(nested.Elements, partType),
            { Nested: not null } => null,
            { Name: { } name } => IrToDartNamingPolicy.ToParameterName(name),
            _ => "_",
        };
}
