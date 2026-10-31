using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads what the IR needs about a C# <c>out</c> parameter: that it is one,
/// and — for a <c>bool</c>-returning method — the value's type on each result
/// as stated by <c>[NotNullWhen]</c> / <c>[MaybeNullWhen]</c>
/// (<c>System.Diagnostics.CodeAnalysis</c>).
/// </summary>
internal static class OutParameterFlow
{
    private const string AttributeNamespace = "System.Diagnostics.CodeAnalysis";

    /// <summary>
    /// <paramref name="parameter"/> with <see cref="IrParameter.IsOut"/> and
    /// <see cref="IrParameter.OutTypesByResult"/> filled from
    /// <paramref name="symbol"/>.
    /// </summary>
    public static IrParameter Annotate(
        IrParameter parameter,
        IParameterSymbol symbol,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        if (symbol.RefKind is not RefKind.Out)
            return parameter;

        return parameter with
        {
            IsOut = true,
            OutTypesByResult = TypesByResult(symbol, originResolver, target),
        };
    }

    /// <summary>
    /// The type a local receiving <paramref name="symbol"/>'s value needs to
    /// hold every value the callee may store: the declared type, made nullable
    /// when <c>[MaybeNullWhen]</c> allows <c>null</c> on some result.
    /// </summary>
    public static ITypeSymbol WidestType(IParameterSymbol symbol) =>
        FindFlowAttribute(symbol) is { MaybeNull: true } && CanBeNull(symbol.Type)
            ? symbol.Type.WithNullableAnnotation(NullableAnnotation.Annotated)
            : symbol.Type;

    /// <summary>
    /// Reports MS0001 when <paramref name="method"/> has <c>out</c> parameters
    /// and an overload with the same other parameters: once the <c>out</c>
    /// parameters move into the result, the two signatures are the same and a
    /// call can no longer tell them apart.
    /// </summary>
    public static void ReportOverloadCollision(IMethodSymbol method)
    {
        if (!method.Parameters.Any(p => p.RefKind is RefKind.Out))
            return;

        var collides = method
            .ContainingType.GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Any(other =>
                !SymbolEqualityComparer.Default.Equals(other, method)
                && other.IsStatic == method.IsStatic
                && InParameterTypes(other)
                    .SequenceEqual(InParameterTypes(method), SymbolEqualityComparer.Default)
            );

        if (!collides)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"Overload '{method.ToDisplayString()}' differs from another '{method.Name}' "
                    + "overload only by its 'out' parameters, which the target returns instead "
                    + "of taking: the two calls cannot be told apart.",
                method.Locations.FirstOrDefault()
            )
        );
    }

    private static IEnumerable<ITypeSymbol> InParameterTypes(IMethodSymbol method) =>
        method.Parameters.Where(p => p.RefKind is not RefKind.Out).Select(p => p.Type);

    private static IrOutTypesByResult? TypesByResult(
        IParameterSymbol symbol,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (
            symbol.ContainingSymbol
            is not IMethodSymbol { ReturnType.SpecialType: SpecialType.System_Boolean }
        )
            return null;

        if (FindFlowAttribute(symbol) is not { } flow)
            return null;

        var declared = IrTypeRefMapper.Map(symbol.Type, originResolver, target);
        var annotated = flow.MaybeNull ? WithNull(declared, symbol.Type) : WithoutNull(declared);

        return flow.ReturnValue
            ? new IrOutTypesByResult(annotated, declared)
            : new IrOutTypesByResult(declared, annotated);
    }

    private static IrTypeRef WithoutNull(IrTypeRef type) =>
        type is IrNullableTypeRef nullable ? nullable.Inner : type;

    private static IrTypeRef WithNull(IrTypeRef type, ITypeSymbol symbol) =>
        type is IrNullableTypeRef || !CanBeNull(symbol) ? type : new IrNullableTypeRef(type);

    /// <summary>
    /// Whether <c>default</c> of <paramref name="type"/> is <c>null</c>: a
    /// reference type or a type parameter not constrained to a struct. A
    /// <c>[MaybeNullWhen]</c> value type still holds its zero value.
    /// </summary>
    private static bool CanBeNull(ITypeSymbol type) =>
        type.IsReferenceType || type is ITypeParameterSymbol { HasValueTypeConstraint: false };

    private static FlowAttribute? FindFlowAttribute(IParameterSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (
                attribute.AttributeClass?.ContainingNamespace.ToDisplayString()
                != AttributeNamespace
            )
                continue;

            if (attribute.ConstructorArguments is not [{ Value: bool returnValue }])
                continue;

            switch (attribute.AttributeClass.Name)
            {
                case "NotNullWhenAttribute":
                    return new FlowAttribute(returnValue, MaybeNull: false);
                case "MaybeNullWhenAttribute":
                    return new FlowAttribute(returnValue, MaybeNull: true);
            }
        }

        return null;
    }

    /// <summary>
    /// <c>[NotNullWhen(ReturnValue)]</c> (<see cref="MaybeNull"/> false) or
    /// <c>[MaybeNullWhen(ReturnValue)]</c> (true).
    /// </summary>
    private sealed record FlowAttribute(bool ReturnValue, bool MaybeNull);
}
