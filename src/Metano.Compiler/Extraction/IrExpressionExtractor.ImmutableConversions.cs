using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A static conversion of an immutable collection to a mutable collection
/// interface (spec 011 FR-054, contracts/runtime-collections.md §
/// Mutable-interface conversions of immutables): .NET's
/// <c>IList&lt;T&gt;</c> / <c>ICollection&lt;T&gt;</c> / <c>ISet&lt;T&gt;</c> /
/// <c>IDictionary&lt;K, V&gt;</c> view of an <c>ImmutableList&lt;T&gt;</c> throws
/// <c>NotSupportedException</c> from its mutators, which a runtime class whose
/// mutators return a new value cannot reproduce, so a mutation through the
/// interface would be dropped silently. Reported as MS0001 for a target whose
/// collections are runtime classes; the value is kept (its reads are sound).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>Reports the implicit conversion of <paramref name="expression"/> when it is one.</summary>
    private void ReportImmutableAsMutableInterface(ExpressionSyntax expression)
    {
        var info = _semantic.GetTypeInfo(expression);

        ReportImmutableAsMutableInterface(expression, info.Type, info.ConvertedType);
    }

    /// <summary>
    /// Reports the conversion of <paramref name="expression"/> from
    /// <paramref name="source"/> to <paramref name="target"/> when it views an
    /// immutable collection as a mutable collection interface.
    /// </summary>
    private void ReportImmutableAsMutableInterface(
        ExpressionSyntax expression,
        ITypeSymbol? source,
        ITypeSymbol? target
    )
    {
        if (
            _context.KeepsNativeCollections
            || KindOf(source?.UnderlyingValueType()) is not { } sourceKind
            || !IrCollectionKinds.IsImmutable(sourceKind)
            || KindOf(target) is not { } targetKind
            || !IrCollectionKinds.IsMutableInterface(targetKind)
        )
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{expression}' converts '{source!.ToDisplayString()}' to "
                    + $"'{target!.ToDisplayString()}': the immutable collection does not "
                    + "implement the mutable interface at run time, so a mutation through it "
                    + "would return a new collection instead of throwing NotSupportedException.",
                expression.GetLocation()
            )
        );
    }

    /// <summary>
    /// Reports the binding of an immutable collection to a type parameter
    /// constrained to a mutable collection interface
    /// (<c>Mutate&lt;T&gt;(T list) where T : IList&lt;int&gt;</c> called with an
    /// <c>ImmutableList&lt;int&gt;</c>): the argument's static type is the
    /// immutable collection itself, so no conversion expression shows it.
    /// </summary>
    private void ReportImmutableAsMutableConstraint(ExpressionSyntax expression)
    {
        if (
            _context.KeepsNativeCollections
            || expression
                is not (
                    InvocationExpressionSyntax
                    or ObjectCreationExpressionSyntax
                    or ImplicitObjectCreationExpressionSyntax
                )
            || _semantic.GetSymbolInfo(expression).Symbol is not IMethodSymbol method
        )
            return;

        var bindings = method
            .OriginalDefinition.TypeParameters.Zip(method.TypeArguments)
            .Concat(
                method.ContainingType.OriginalDefinition.TypeParameters.Zip(
                    method.ContainingType.TypeArguments
                )
            );

        foreach (var (parameter, argument) in bindings)
        {
            foreach (var constraint in parameter.ConstraintTypes)
                ReportImmutableAsMutableInterface(expression, argument, constraint);
        }
    }

    /// <summary>
    /// Reports a <em>binding</em> test (<c>x as T</c>, <c>x is T name</c>, a switch
    /// arm <c>T name =></c>) of <paramref name="operand"/> against
    /// <paramref name="tested"/> when it asks an immutable collection whether it is
    /// a mutable collection interface (MS0001, contracts/runtime-collections.md §
    /// Mutable-interface conversions of immutables): the binding it opens would
    /// mutate through <c>add</c> / <c>set</c> methods that return a new collection
    /// and drop it, where .NET throws <c>NotSupportedException</c>. The caller
    /// lowers the test to a placeholder. A test that binds nothing
    /// (<c>x is ISet&lt;int&gt;</c>) is sound, answers <c>true</c> as .NET does and
    /// is not reported. <c>false</c> when the operand cannot hold an immutable
    /// collection or the target is not a mutable interface.
    /// </summary>
    private bool ReportsImmutableTestedAsMutableInterface(
        SyntaxNode test,
        ITypeSymbol? tested,
        ITypeSymbol? operand
    )
    {
        if (
            _context.KeepsNativeCollections
            || KindOf(tested) is not { } testedKind
            || !IrCollectionKinds.IsMutableInterface(testedKind)
            || operand is null
            || !CanHoldImmutableCollection(operand)
        )
            return false;

        ReportUnsupportedFeature(
            test,
            $"'{test}' tests whether a value of '{operand.ToDisplayString()}' is "
                + $"'{tested!.ToDisplayString()}': an immutable collection answers true as in .NET, "
                + "but does not implement the mutable interface at run time, so a mutation through "
                + "the match would return a new collection instead of throwing NotSupportedException."
        );

        return true;
    }

    /// <summary>
    /// Whether a value of static type <paramref name="operand"/> can be an
    /// immutable collection without a conversion that is itself reported:
    /// <c>object</c>, a type parameter, <c>IEnumerable</c>, an immutable
    /// collection or one of the read-only and immutable interfaces it
    /// implements. (A mutable interface operand holds one only through a
    /// reported conversion.)
    /// </summary>
    private static bool CanHoldImmutableCollection(ITypeSymbol operand)
    {
        if (
            operand is ITypeParameterSymbol
            || operand.OriginalDefinition.SpecialType
                is SpecialType.System_Object
                    or SpecialType.System_Collections_IEnumerable
                    or SpecialType.System_Collections_Generic_IEnumerable_T
        )
            return true;

        return KindOf(operand.UnderlyingValueType()) is { } kind
            && (
                IrCollectionKinds.IsImmutable(kind)
                || kind
                    is IrCollectionKind.IImmutableList
                        or IrCollectionKind.IImmutableSet
                        or IrCollectionKind.IImmutableDictionary
                        or IrCollectionKind.IReadOnlyList
                        or IrCollectionKind.IReadOnlyCollection
                        or IrCollectionKind.IReadOnlyDictionary
                        or IrCollectionKind.IReadOnlySet
            );
    }

    private static IrCollectionKind? KindOf(ITypeSymbol? type) =>
        type is INamedTypeSymbol named ? CollectionKindResolver.KindOf(named) : null;
}
