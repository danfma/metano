using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The default text of primitives, erased values, enums, tuples and TimeSpan (baseline FR-014,
/// #308): a target whose own conversion differs from .NET's (<c>true</c> for <c>True</c>,
/// <c>1e-7</c> for <c>1E-07</c>) declaratively maps the parameterless <c>ToString()</c> of
/// these types to a runtime function. Interpolation, string concatenation and
/// <c>string.Join</c> convert a value of these static types as <c>value.ToString()</c>
/// so that mapping applies, and every other type, and every target without the mapping,
/// keeps its own conversion.
/// </summary>
internal static class DefaultTextConversion
{
    /// <summary>
    /// The parameterless <c>ToString()</c> of <paramref name="type"/> (looking through
    /// <c>Nullable&lt;T&gt;</c>) when it needs invariant default text and
    /// the target maps it; <c>null</c> otherwise.
    /// </summary>
    public static IMethodSymbol? MappedToString(
        ITypeSymbol? type,
        Func<IMethodSymbol, int, bool>? lowersByDeclarativeMapping
    )
    {
        if (type?.UnwrapNullableValueType() is not { } value || !HasInvariantText(value))
            return null;

        var toString = ParameterlessToString(value);

        return toString is not null && lowersByDeclarativeMapping?.Invoke(toString, 0) == true
            ? toString
            : null;
    }

    /// <summary><c>value.ToString()</c> as call extraction builds it, so the target's mapping lowers it.</summary>
    public static IrCallExpression Call(
        IrExpression value,
        IMethodSymbol toString,
        IrMemberOrigin? origin
    ) =>
        new(
            new IrMemberAccess(
                toString.ContainingType.SpecialType == SpecialType.System_Object
                    ? new IrBinaryExpression(
                        value,
                        IrBinaryOp.NullCoalescing,
                        new IrLiteral("", IrLiteralKind.String)
                    )
                    : value,
                toString.Name,
                origin
            ),
            [],
            Origin: origin
        );

    private static IMethodSymbol? ParameterlessToString(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (
                current
                    .GetMembers(nameof(ToString))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method =>
                        method is { IsStatic: false, Parameters.Length: 0 }
                    ) is
                { } method
            )
                return method;
        }

        return null;
    }

    private static bool HasInvariantText(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            && IrTypeRefMapper.IsTranspilableType(enumType, target: null)
        || type.TypeKind == TypeKind.TypeParameter
        || type is INamedTypeSymbol { IsTupleType: true }
        || type.ToDisplayString() == "System.TimeSpan"
        || type.SpecialType
            is SpecialType.System_Boolean
                or SpecialType.System_Double
                or SpecialType.System_Single
                or SpecialType.System_Decimal
                or SpecialType.System_Object;
}
