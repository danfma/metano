using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The tuple-type facts the value-equality lowerings share: unwrapping a
/// nullable value (<c>(int, int)?</c>) to the tuple it may hold, telling
/// whether two types are the same <c>ValueTuple</c> instantiation, and
/// whether a type provably cannot hold a given tuple.
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
        var unwrapped = type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
            ? nullable.TypeArguments[0]
            : type;

        return unwrapped is INamedTypeSymbol { IsTupleType: true } tuple ? tuple : null;
    }

    /// <summary>Whether <paramref name="type"/> is a nullable tuple.</summary>
    internal static bool IsNullableTuple(ITypeSymbol? type) =>
        type is not null && IsNullableValue(type) && UnderlyingTupleOf(type) is not null;

    /// <summary>
    /// Whether a value of <paramref name="type"/> can be <c>null</c>: a
    /// non-nullable value type never can, everything else (a reference type,
    /// a nullable value type, a type parameter, <c>object</c>) can.
    /// </summary>
    internal static bool MayBeNull(ITypeSymbol? type) =>
        type is not { IsValueType: true } || IsNullableValue(type);

    /// <summary>
    /// Whether a value of <paramref name="type"/> provably cannot hold
    /// <paramref name="tuple"/> boxed, so C#'s value equality between the two
    /// is <c>false</c> unless both are <c>null</c>: a concrete value type other
    /// than the tuple's own instantiation (its boxing is that value, never the
    /// tuple), or a class other than <c>object</c> and <c>ValueType</c> (no
    /// class derives from a tuple). <c>object</c>, <c>ValueType</c>, an
    /// interface, <c>dynamic</c>, a type parameter (constrained to
    /// <c>struct</c> or not) and <c>Nullable</c> of a type parameter may all
    /// hold it; so may an operand without a type (the <c>null</c> literal).
    /// </summary>
    internal static bool CannotHoldTuple(ITypeSymbol? type, INamedTypeSymbol tuple)
    {
        if (type is null || IsSameInstantiation(UnderlyingTupleOf(type), tuple, out _))
            return false;

        return type switch
        {
            ITypeParameterSymbol => false,
            _ when IsNullableOfTypeParameter(type) => false,
            { IsValueType: true } => true,
            { TypeKind: TypeKind.Class or TypeKind.Array or TypeKind.Delegate } =>
                !IsObjectOrValueType(type),
            _ => false,
        };
    }

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> are the
    /// same <c>ValueTuple</c> instantiation: element names are compile-time
    /// only, so <c>(int x, int y)</c> and <c>(int p, int q)</c> are one type,
    /// but a nullable tuple element (<c>(int, int)?</c>) is a different
    /// element type than the tuple itself. Compared element by element
    /// (recursing through tuple elements), since symbol equality itself keeps
    /// the names of a nested tuple element.
    /// </summary>
    internal static bool IsSameInstantiation(
        INamedTypeSymbol? first,
        INamedTypeSymbol? second,
        [NotNullWhen(true)] out INamedTypeSymbol? tuple
    )
    {
        tuple = null;

        if (
            first is not { IsTupleType: true }
            || second is not { IsTupleType: true }
            || first.TupleElements.Length != second.TupleElements.Length
        )
        {
            return false;
        }

        for (var i = 0; i < first.TupleElements.Length; i++)
        {
            if (!IsSameElement(first.TupleElements[i].Type, second.TupleElements[i].Type))
                return false;
        }

        tuple = first;
        return true;
    }

    private static bool IsSameElement(ITypeSymbol first, ITypeSymbol second)
    {
        var firstIsNullable = IsNullableValue(first);
        var secondIsNullable = IsNullableValue(second);

        // A nullable tuple element is the tuple or null — a different element
        // type than the plain tuple — so the nullability must agree.
        if (firstIsNullable != secondIsNullable)
            return false;

        if (UnderlyingTupleOf(first) is { } firstTuple)
        {
            return IsSameInstantiation(firstTuple, UnderlyingTupleOf(second), out _);
        }

        return SymbolEqualityComparer.Default.Equals(first, second);
    }

    private static bool IsNullableOfTypeParameter(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeArguments: [ITypeParameterSymbol] } && IsNullableValue(type);

    private static bool IsObjectOrValueType(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType;

    private static bool IsNullableValue(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
}
