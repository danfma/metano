using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Gives the element-taking members of the immutable collections one array
/// argument (spec 011 FR-054, contracts/runtime-collections.md § C# member →
/// emitted TS, the of/from rule): the elements C# gathers into a fresh array
/// (the element overloads <c>Create(a, b)</c> and an expanded <c>params</c>
/// call, <see cref="ArgumentKind.ParamArray"/> or
/// <see cref="ArgumentKind.ParamCollection"/>) become one
/// <see cref="IrArrayLiteral"/>, while an existing array or span passed in
/// normal form stays the value it is, never spread. A <c>Create</c> passed
/// such a value is the <c>CreateRange</c> of it, which copies it as
/// <c>Create</c> does. The shapes are neutral: they say which argument is a
/// fresh array, so a target may take ownership of it and of nothing else.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    private const string CreateRangeName = "CreateRange";

    /// <summary>The static factory classes whose <c>Create</c> overloads take elements.</summary>
    private static readonly string[] ImmutableFactoryClasses =
    [
        "System.Collections.Immutable.ImmutableList",
        "System.Collections.Immutable.ImmutableArray",
        "System.Collections.Immutable.ImmutableHashSet",
    ];

    /// <summary>The types whose <c>params</c> range members take an iterable at run time.</summary>
    private static readonly string[] ImmutableRangeOwners =
    [
        "System.Collections.Immutable.ImmutableArray`1",
        "System.Collections.Immutable.ImmutableArray`1+Builder",
    ];

    private void SeedImmutableFactories(Compilation compilation)
    {
        foreach (var name in ImmutableFactoryClasses)
        {
            var factories = compilation.GetTypeByMetadataName(name);

            foreach (var create in ElementCreates(factories))
                _entries[create] = BuildImmutableCreateCall;
        }

        foreach (var name in ImmutableRangeOwners)
        {
            var owner = compilation.GetTypeByMetadataName(name);

            foreach (var range in ParamsMembers(owner))
                _entries[range] = BuildParamsRangeCall;
        }
    }

    /// <summary>
    /// The <c>Create</c> overloads that take elements: every parameter but a
    /// leading comparer is an element (<c>T</c>) or the element sequence
    /// (<c>T[]</c>, a span); never the empty, comparer-only or range ones.
    /// </summary>
    private static IEnumerable<IMethodSymbol> ElementCreates(INamedTypeSymbol? factories) =>
        factories
            ?.GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Where(create =>
                create.TypeParameters is [var element]
                && ElementParametersOf(create) is [_, ..] elements
                && elements.All(parameter => IsElementOrSequenceOf(parameter.Type, element))
            )
        ?? [];

    private static IEnumerable<IMethodSymbol> ParamsMembers(INamedTypeSymbol? owner) =>
        owner
            ?.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.Parameters is [.., { IsParams: true }])
        ?? [];

    private static IReadOnlyList<IParameterSymbol> ElementParametersOf(IMethodSymbol create) =>
        [.. create.Parameters.SkipWhile(IsComparerParameter)];

    private static bool IsComparerParameter(IParameterSymbol parameter) =>
        parameter.Type.OriginalDefinition.MetadataName == "IEqualityComparer`1";

    private static bool IsElementOrSequenceOf(ITypeSymbol type, ITypeParameterSymbol element) =>
        type switch
        {
            ITypeParameterSymbol parameter => SymbolEqualityComparer.Default.Equals(
                parameter,
                element
            ),
            IArrayTypeSymbol array => SymbolEqualityComparer.Default.Equals(
                array.ElementType,
                element
            ),
            INamedTypeSymbol { MetadataName: "ReadOnlySpan`1" or "Span`1" } span =>
                SymbolEqualityComparer.Default.Equals(span.TypeArguments[0], element),
            _ => false,
        };

    /// <summary>
    /// <c>Create(…)</c> over elements → <c>Create([comparer, ]params T[])</c>
    /// with one fresh array; over an existing array or span →
    /// <c>CreateRange([comparer, ]value)</c>. Declines for a target that keeps
    /// native collections, whose 2.x mapping keeps the source shape.
    /// </summary>
    private IrExpression? BuildImmutableCreateCall(InvocationRewriteContext ctx)
    {
        if (IrExtractionContext.KeepsNativeCollectionsFor(_helpers.Target))
            return null;

        var create = ctx.Symbol!;
        var arguments = ArgumentsInParameterOrder(BoundInvocation(ctx));
        var comparer = arguments.Where(argument => IsComparerParameter(argument.Parameter!));
        var leading = comparer.Select(argument => new IrArgument(ArgumentValue(ctx, argument)));

        var elements = arguments
            .Where(argument => !IsComparerParameter(argument.Parameter!))
            .ToList();

        var (canonical, value) =
            elements is [var sequence] && IsExistingSequence(sequence)
                ? (
                    CanonicalOverload(create, CreateRangeName, IsEnumerable),
                    ArgumentValue(ctx, sequence)
                )
                : (CanonicalOverload(create, create.Name, IsArray), FreshArrayOf(ctx, elements));

        return new IrCallExpression(
            new IrMemberAccess(FactoryReceiverOf(ctx), canonical.Name),
            [.. leading, new IrArgument(value)],
            [.. create.TypeArguments.Select(_helpers.MapTypeRef)],
            _helpers.BuildOrigin(canonical)
        );
    }

    /// <summary>
    /// A <c>params</c> range member of <c>ImmutableArray&lt;T&gt;</c> or its
    /// builder (<c>AddRange(a, b)</c>, <c>InsertRange(i, a, b)</c>): the
    /// expanded elements as one fresh array, a normal-form array or span
    /// unchanged and never spread (the runtime member takes an iterable).
    /// </summary>
    private IrExpression? BuildParamsRangeCall(InvocationRewriteContext ctx)
    {
        if (IrExtractionContext.KeepsNativeCollectionsFor(_helpers.Target))
            return null;

        var member = ctx.Symbol!;
        var arguments = ArgumentsInParameterOrder(BoundInvocation(ctx));

        return new IrCallExpression(
            new IrMemberAccess(ReceiverOf(ctx), member.Name),
            [
                .. arguments.Select(argument => new IrArgument(
                    argument.ArgumentKind is ArgumentKind.Explicit
                        ? ArgumentValue(ctx, argument)
                        : FreshArrayOf(ctx, [argument])
                )),
            ],
            Origin: _helpers.BuildOrigin(member)
        );
    }

    /// <summary>
    /// An argument that passes an existing array or span in normal form: an
    /// explicit argument for a sequence parameter.
    /// </summary>
    private static bool IsExistingSequence(IArgumentOperation argument) =>
        argument.ArgumentKind is ArgumentKind.Explicit
        && argument.Parameter!.OriginalDefinition.Type is not ITypeParameterSymbol;

    /// <summary>
    /// The fresh array C# builds from <paramref name="elements"/>: each
    /// element argument, or the elements of an expanded <c>params</c> one.
    /// </summary>
    private static IrArrayLiteral FreshArrayOf(
        InvocationRewriteContext ctx,
        IReadOnlyList<IArgumentOperation> elements
    ) =>
        new([
            .. elements
                .SelectMany(ExpandedValuesOf)
                .Select(value => ctx.Recurse((ExpressionSyntax)value.Syntax)),
        ]);

    private static IEnumerable<IOperation> ExpandedValuesOf(IArgumentOperation argument) =>
        argument switch
        {
            { ArgumentKind: ArgumentKind.Explicit } => [argument.Value],
            { Value: IArrayCreationOperation { Initializer: { } initializer } } =>
                initializer.ElementValues,
            { Value: ICollectionExpressionOperation collection } => collection.Elements,
            _ => [],
        };

    private static IrExpression ArgumentValue(
        InvocationRewriteContext ctx,
        IArgumentOperation argument
    ) => ctx.Recurse((ExpressionSyntax)argument.Value.Syntax);

    /// <summary>
    /// The overload of <paramref name="create"/>'s class named
    /// <paramref name="name"/> with the same leading comparers and a sequence
    /// parameter <paramref name="isSequence"/> accepts, constructed with
    /// <paramref name="create"/>'s type arguments.
    /// </summary>
    private static IMethodSymbol CanonicalOverload(
        IMethodSymbol create,
        string name,
        Func<ITypeSymbol, bool> isSequence
    )
    {
        var comparers = create.Parameters.Count(IsComparerParameter);

        var canonical = create
            .ContainingType.GetMembers(name)
            .OfType<IMethodSymbol>()
            .First(candidate =>
                candidate.TypeParameters.Length == 1
                && candidate.Parameters.Length == comparers + 1
                && candidate.Parameters.Take(comparers).All(IsComparerParameter)
                && isSequence(candidate.Parameters[^1].Type)
            );

        return canonical.Construct([.. create.TypeArguments]);
    }

    private static bool IsEnumerable(ITypeSymbol type) =>
        type.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T;

    private static bool IsArray(ITypeSymbol type) => type is IArrayTypeSymbol;

    /// <summary>The static class a factory call names, as the call writes it.</summary>
    private static IrExpression FactoryReceiverOf(InvocationRewriteContext ctx) =>
        QualifiedMemberSyntax.From(ctx.Invocation.Expression)?.Receiver is { } receiver
            ? ctx.Recurse(receiver)
            : new IrTypeReference(ctx.Symbol!.ContainingType.Name);
}
