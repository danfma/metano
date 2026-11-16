using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The default text of a <c>bool</c>, <c>double</c> and <c>float</c> (baseline FR-014,
/// #308): a target whose own conversion differs from .NET's (<c>true</c> for <c>True</c>,
/// <c>1e-7</c> for <c>1E-07</c>) declaratively maps the parameterless <c>ToString()</c> of
/// these types to a runtime function. Interpolation, string concatenation and
/// <c>string.Join</c> convert a value of one of these static types as <c>value.ToString()</c>
/// so that mapping applies, and every other type, and every target without the mapping,
/// keeps its own conversion.
/// </summary>
internal static class DefaultTextConversion
{
    /// <summary>
    /// The parameterless <c>ToString()</c> of <paramref name="type"/> (looking through
    /// <c>Nullable&lt;T&gt;</c>) when it is a <c>bool</c>, <c>double</c> or <c>float</c> and
    /// the target maps it; <c>null</c> otherwise.
    /// </summary>
    public static IMethodSymbol? MappedToString(
        ITypeSymbol? type,
        Func<IMethodSymbol, int, bool>? lowersByDeclarativeMapping
    )
    {
        if (type?.UnwrapNullableValueType() is not { } value || !HasInvariantText(value))
            return null;

        var toString = value
            .GetMembers(nameof(ToString))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method is { IsStatic: false, Parameters.Length: 0 });

        return toString is not null && lowersByDeclarativeMapping?.Invoke(toString, 0) == true
            ? toString
            : null;
    }

    /// <summary><c>value.ToString()</c> as call extraction builds it, so the target's mapping lowers it.</summary>
    public static IrCallExpression Call(
        IrExpression value,
        IMethodSymbol toString,
        IrMemberOrigin? origin
    ) => new(new IrMemberAccess(value, toString.Name, origin), [], Origin: origin);

    private static bool HasInvariantText(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_Boolean
                or SpecialType.System_Double
                or SpecialType.System_Single;
}
