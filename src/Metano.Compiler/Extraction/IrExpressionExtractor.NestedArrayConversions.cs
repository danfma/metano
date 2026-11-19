using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The C# conversions that would hand a native array to code typed as a list
/// interface without the top-level conversion of spec 011 FR-003, so no
/// <c>List.adopt</c> can be inserted (data-model § 2, B3): a variant reference,
/// delegate or tuple conversion with an array where the target has a list
/// interface in a type-argument position (reason 1), and a generic method or
/// type instantiated with an array for a type parameter constrained to a list
/// interface (reason 2). Each reports MS0001 for a target whose collections are
/// runtime classes: the value would run <c>count</c> / <c>get</c> on an array,
/// a silent <c>undefined</c>. A target that keeps native collections never
/// runs the check.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Reports <paramref name="expression"/> when its implicit conversion or
    /// its generic instantiation is one of the B3 shapes.
    /// </summary>
    private void ReportNestedArrayAsListInterface(ExpressionSyntax expression)
    {
        var info = _semantic.GetTypeInfo(expression);

        if (
            info.Type is { } source
            && info.ConvertedType is { } target
            && !SymbolEqualityComparer.Default.Equals(source, target)
            && NestsArrayAsListInterface(source, target, depth: 0, nested: false)
        )
        {
            ReportNestedArray(
                expression,
                $"'{expression}' converts '{source.ToDisplayString()}' to "
                    + $"'{target.ToDisplayString()}': a nested array cannot be viewed as a list "
                    + "interface. Build the collections explicitly (ToList())."
            );

            return;
        }

        if (ArrayBoundToListInterfaceParameter(expression) is { } typeParameter)
            ReportNestedArray(
                expression,
                $"'{expression}' instantiates '{typeParameter.Name}' with an array, but "
                    + $"'{typeParameter.Name}' is constrained to a list interface, which an array "
                    + "is not at run time. Pass a List<T> instead."
            );
    }

    private static void ReportNestedArray(ExpressionSyntax expression, string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                message,
                expression.GetLocation()
            )
        );

    /// <summary>
    /// Whether converting <paramref name="source"/> to
    /// <paramref name="target"/> puts an array where the target has a list
    /// interface below the top level (the top level itself is FR-003's
    /// conversion).
    /// </summary>
    private static bool NestsArrayAsListInterface(
        ITypeSymbol source,
        ITypeSymbol target,
        int depth,
        bool nested
    )
    {
        if (depth > 16)
            return false;

        if (
            nested
            && source is IArrayTypeSymbol
            && target is INamedTypeSymbol named
            && IsListInterface(named)
        )
            return true;

        return (source, target) switch
        {
            (IArrayTypeSymbol sourceArray, IArrayTypeSymbol targetArray) =>
                NestsArrayAsListInterface(
                    sourceArray.ElementType,
                    targetArray.ElementType,
                    depth + 1,
                    nested: true
                ),
            (IArrayTypeSymbol sourceArray, INamedTypeSymbol targetNamed) => ElementNestsArray(
                sourceArray,
                targetNamed,
                depth
            ),
            (INamedTypeSymbol sourceNamed, INamedTypeSymbol targetNamed) => ArgumentsNestArray(
                sourceNamed,
                targetNamed,
                depth
            ),
            _ => false,
        };
    }

    /// <summary>
    /// An array converted to a sequence type (<c>int[][]</c> to
    /// <c>IReadOnlyList&lt;IReadOnlyList&lt;int&gt;&gt;</c> or
    /// <c>IEnumerable&lt;IList&lt;int&gt;&gt;</c>): its element type against
    /// the element type of the <c>IEnumerable&lt;T&gt;</c> the target is or
    /// implements. Only the outer array is adopted, so an inner array that
    /// reaches a list interface is a B3 shape.
    /// </summary>
    private static bool ElementNestsArray(
        IArrayTypeSymbol source,
        INamedTypeSymbol target,
        int depth
    ) =>
        SequenceElementTypeOf(target) is { } targetElement
        && NestsArrayAsListInterface(source.ElementType, targetElement, depth + 1, nested: true);

    private static ITypeSymbol? SequenceElementTypeOf(INamedTypeSymbol type)
    {
        var sequence =
            type.ConstructedFrom.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
                ? type
                : type.AllInterfaces.FirstOrDefault(candidate =>
                    candidate.ConstructedFrom.SpecialType
                    is SpecialType.System_Collections_Generic_IEnumerable_T
                );

        return sequence?.TypeArguments[0];
    }

    private static bool ArgumentsNestArray(
        INamedTypeSymbol source,
        INamedTypeSymbol target,
        int depth
    )
    {
        var pair = SameDefinitionPair(source, target);

        return pair is var (from, to)
            && from.TypeArguments.Zip(to.TypeArguments)
                .Any(arguments =>
                    NestsArrayAsListInterface(
                        arguments.First,
                        arguments.Second,
                        depth + 1,
                        nested: true
                    )
                );
    }

    /// <summary>
    /// The construction of <paramref name="target"/>'s definition that
    /// <paramref name="source"/> is or implements (a variant conversion from
    /// <c>List&lt;int[]&gt;</c> to <c>IEnumerable&lt;IReadOnlyList&lt;int&gt;&gt;</c>
    /// goes through <c>IEnumerable&lt;int[]&gt;</c>).
    /// </summary>
    private static (INamedTypeSymbol, INamedTypeSymbol)? SameDefinitionPair(
        INamedTypeSymbol source,
        INamedTypeSymbol target
    )
    {
        var definition = target.OriginalDefinition;

        var match = SymbolEqualityComparer.Default.Equals(source.OriginalDefinition, definition)
            ? source
            : source.AllInterfaces.FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, definition)
            );

        return match is null ? null : (match, target);
    }

    /// <summary>
    /// The type parameter a generic call or construction binds to an array
    /// (or a type containing one) although its constraints include a list
    /// interface; <c>null</c> when there is none.
    /// </summary>
    private ITypeParameterSymbol? ArrayBoundToListInterfaceParameter(ExpressionSyntax expression)
    {
        if (expression is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
            return null;

        var (parameters, arguments) = _semantic.GetSymbolInfo(expression).Symbol switch
        {
            IMethodSymbol { IsGenericMethod: true } method
                when expression is InvocationExpressionSyntax => (
                method.TypeParameters,
                method.TypeArguments
            ),
            IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                when expression is BaseObjectCreationExpressionSyntax => (
                constructor.ContainingType.TypeParameters,
                constructor.ContainingType.TypeArguments
            ),
            _ => (default, default),
        };

        if (parameters.IsDefaultOrEmpty)
            return null;

        return parameters
            .Zip(arguments)
            .Where(pair => ContainsArray(pair.Second, depth: 0))
            .Select(pair => pair.First)
            .FirstOrDefault(parameter =>
                parameter.ConstraintTypes.OfType<INamedTypeSymbol>().Any(IsListInterface)
            );
    }

    private static bool ContainsArray(ITypeSymbol type, int depth) =>
        depth <= 16
        && type switch
        {
            IArrayTypeSymbol => true,
            INamedTypeSymbol named => named.TypeArguments.Any(argument =>
                ContainsArray(argument, depth + 1)
            ),
            _ => false,
        };
}
