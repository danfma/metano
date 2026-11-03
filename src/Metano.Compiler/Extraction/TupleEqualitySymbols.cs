using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The tuple-type facts the value-equality lowerings share: unwrapping a
/// nullable value (<c>(int, int)?</c>) to the tuple it may hold, and telling
/// whether two types are the same <c>ValueTuple</c> instantiation.
/// </summary>
internal static class TupleEqualitySymbols
{
    /// <summary>
    /// The tuple <paramref name="type"/> holds — itself when it is a tuple,
    /// the underlying tuple of a nullable one; <c>null</c> for any other
    /// type.
    /// </summary>
    internal static INamedTypeSymbol? UnderlyingTupleOf(ITypeSymbol? type)
    {
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
        )
        {
            type = nullable.TypeArguments[0];
        }

        return type is INamedTypeSymbol { IsTupleType: true } tuple ? tuple : null;
    }

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> are the
    /// same <c>ValueTuple</c> instantiation: element names are compile-time
    /// only, so <c>(int x, int y)</c> and <c>(int p, int q)</c> are one type.
    /// Compared element by element (recursing through tuple elements), since
    /// symbol equality itself keeps the names of a nested tuple element.
    /// </summary>
    internal static bool IsSameInstantiation(INamedTypeSymbol? first, INamedTypeSymbol? second)
    {
        if (first is not { IsTupleType: true } || second is not { IsTupleType: true })
            return false;

        if (first.TupleElements.Length != second.TupleElements.Length)
            return false;

        for (var i = 0; i < first.TupleElements.Length; i++)
        {
            if (!IsSameElement(first.TupleElements[i].Type, second.TupleElements[i].Type))
                return false;
        }

        return true;
    }

    private static bool IsSameElement(ITypeSymbol first, ITypeSymbol second)
    {
        if (UnderlyingTupleOf(first) is { } firstTuple)
        {
            return IsSameInstantiation(firstTuple, UnderlyingTupleOf(second));
        }

        return SymbolEqualityComparer.Default.Equals(first, second);
    }
}
