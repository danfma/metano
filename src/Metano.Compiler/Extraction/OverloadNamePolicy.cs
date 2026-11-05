using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The kind of a dispatched overload group, which decides where naming starts
/// (contracts/naming.md § Stage 1).
/// </summary>
internal enum OverloadGroupKind
{
    /// <summary>Methods and module functions: parameter names first.</summary>
    Method,

    /// <summary>
    /// Extension methods: parameter names first, and a receiver every overload
    /// declares with the same name and type takes no part in any pass.
    /// </summary>
    ExtensionMethod,

    /// <summary>User-defined operators: always the type spellings.</summary>
    Operator,
}

/// <summary>
/// One overload as the naming policy sees it.
/// </summary>
/// <param name="Parameters">Its parameters, each carrying its
/// <see cref="IrParameter.NamingSpelling"/>.</param>
/// <param name="ConversionTarget">A conversion operator's target type, which
/// C# tells conversions apart by (<c>explicit operator int(Money m)</c> /
/// <c>explicit operator long(Money m)</c> → <c>MoneyToInt32</c> /
/// <c>MoneyToInt64</c>); <c>null</c> for any other overload.</param>
internal sealed record OverloadSignature(
    IReadOnlyList<IrParameter> Parameters,
    IrNamingSpelling? ConversionTarget = null
);

/// <summary>
/// The suffixes <see cref="OverloadNamePolicy"/> computed for one group, in the
/// group's order.
/// </summary>
/// <param name="Suffixes">One suffix per overload.</param>
/// <param name="DeadEnds">The sets of overloads (indexes into the group, in
/// declaration order) the climb could not separate; each carries a positional
/// marker and the caller reports <c>MS0003</c> for it (FR-024).</param>
internal sealed record OverloadSuffixes(
    IReadOnlyList<string> Suffixes,
    IReadOnlyList<IReadOnlyList<int>> DeadEnds
);

/// <summary>
/// Stage 1 of the overload naming algorithm (spec 013 FR-001–FR-003, FR-006,
/// FR-024; contracts/naming.md): computes one suffix per overload of a
/// dispatched group from its parameter names and the
/// <see cref="IrParameter.NamingSpelling"/> captured from Roslyn, never from the
/// IR type refs.
/// <list type="number">
///   <item>Parameter names, no joiner (<c>WidthHeight</c>); <c>Default</c> for a
///   parameterless overload.</item>
///   <item>On any collision the whole group switches to the type spellings
///   (<c>Int32</c>, <c>UInt32</c>, <c>ListOfInt32</c>).</item>
///   <item>While overloads still share a suffix, each colliding set climbs one
///   qualifier level of its parameter types (<c>NumericsDecimal</c>).</item>
///   <item>A set that can climb no further keeps positional markers
///   (<c>$1</c>, <c>$2</c>) in declaration order.</item>
/// </list>
/// A pure function of its input: the same group always gets the same suffixes.
/// The targets claim the names derived from the suffixes (DECIDED-18).
/// </summary>
internal static class OverloadNamePolicy
{
    private const string ParameterlessSuffix = "Default";

    /// <param name="overloads">The overloads, in declaration order.</param>
    /// <param name="kind">Where naming starts.</param>
    public static OverloadSuffixes Compute(
        IReadOnlyList<OverloadSignature> overloads,
        OverloadGroupKind kind
    )
    {
        var participating = WithoutSharedReceiver(overloads, kind);

        if (kind != OverloadGroupKind.Operator)
        {
            var byParameterName = participating
                .Select(overload => ParameterNameSuffix(overload.Parameters))
                .ToList();

            if (AreDistinct(byParameterName))
                return new OverloadSuffixes(byParameterName, []);
        }

        return OverloadSpellingClimb.Run(participating.Select(SpellingComponents).ToList());
    }

    /// <summary>
    /// An extension group leaves its receiver out of every pass when every
    /// overload declares it with the same name and spelling
    /// (<c>Twice(this Base b, int x)</c> / <c>Twice(this Base b, string s)</c> →
    /// <c>X</c> / <c>S</c>). A nullable annotation is part of the spelling, so
    /// <c>this string? s</c> and <c>this string s</c> are different receivers.
    /// </summary>
    private static IReadOnlyList<OverloadSignature> WithoutSharedReceiver(
        IReadOnlyList<OverloadSignature> overloads,
        OverloadGroupKind kind
    )
    {
        if (kind != OverloadGroupKind.ExtensionMethod || !ShareReceiver(overloads))
            return overloads;

        return overloads
            .Select(overload => overload with { Parameters = overload.Parameters.Skip(1).ToList() })
            .ToList();
    }

    private static bool ShareReceiver(IReadOnlyList<OverloadSignature> overloads)
    {
        var receiver = overloads[0].Parameters.FirstOrDefault();

        return receiver is not null
            && overloads.All(overload => DeclaresReceiver(overload.Parameters, receiver));
    }

    private static bool DeclaresReceiver(
        IReadOnlyList<IrParameter> parameters,
        IrParameter receiver
    ) =>
        parameters.Count > 0
        && parameters[0].Name == receiver.Name
        && SpellingOf(parameters[0]).SpellsSameTypeAs(SpellingOf(receiver));

    private static string ParameterNameSuffix(IReadOnlyList<IrParameter> parameters) =>
        parameters.Count == 0
            ? ParameterlessSuffix
            : string.Concat(parameters.Select(parameter => NamingText.Capitalize(parameter.Name)));

    /// <summary>
    /// The overload's type spelling parts: its parameters' parts (or
    /// <c>Default</c>), then <c>To</c> and a conversion's target type.
    /// </summary>
    private static IReadOnlyList<IrNamingComponent> SpellingComponents(OverloadSignature overload)
    {
        IReadOnlyList<IrNamingComponent> parameterParts =
            overload.Parameters.Count == 0
                ? [new IrNamingComponent(ParameterlessSuffix, [])]
                : overload
                    .Parameters.SelectMany(parameter => SpellingOf(parameter).Components)
                    .ToList();

        if (overload.ConversionTarget is not { } target)
            return parameterParts;

        return [.. parameterParts, new IrNamingComponent("To", []), .. target.Components];
    }

    private static IrNamingSpelling SpellingOf(IrParameter parameter) =>
        parameter.NamingSpelling
        ?? throw new InvalidOperationException(
            $"Parameter '{parameter.Name}' of a dispatched overload group has no naming spelling."
        );

    private static bool AreDistinct(IReadOnlyList<string> suffixes) =>
        suffixes.Distinct(StringComparer.Ordinal).Count() == suffixes.Count;
}
