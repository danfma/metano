using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The JSON collection kinds of the stage 011h collections (spec 011 FR-028,
/// contracts/runtime-collections.md § JSON kinds): each widens the
/// <c>kind</c> of the sequence or keyed descriptor its shape classifies to
/// (<see cref="JsonPropertyKind.List"/>, <see cref="JsonPropertyKind.HashSet"/>,
/// <see cref="JsonPropertyKind.Dictionary"/>) and builds its runtime class in
/// its <c>create</c> closure, as System.Text.Json reads it: a repeated key
/// keeps its last value, a sorted set ignores duplicates, and a
/// <c>ReadOnlyCollection&lt;T&gt;</c> serializes but cannot be read.
/// </summary>
internal static class JsonStageHCollections
{
    private const string ExceptionsModule = "metano-runtime/system/exceptions";

    /// <summary>The wire kind of each stage 011h collection kind.</summary>
    private static readonly IReadOnlyDictionary<IrCollectionKind, string> WireKinds =
        new Dictionary<IrCollectionKind, string>
        {
            [IrCollectionKind.ImmutableList] = "immutableList",
            [IrCollectionKind.IImmutableList] = "immutableList",
            [IrCollectionKind.ImmutableArray] = "immutableArray",
            [IrCollectionKind.ReadOnlyCollection] = "readOnlyList",
            [IrCollectionKind.ImmutableHashSet] = "immutableHashSet",
            [IrCollectionKind.IImmutableSet] = "immutableHashSet",
            [IrCollectionKind.SortedSet] = "sortedSet",
            [IrCollectionKind.ImmutableDictionary] = "immutableDictionary",
            [IrCollectionKind.IImmutableDictionary] = "immutableDictionary",
            [IrCollectionKind.ConcurrentDictionary] = "concurrentDictionary",
            [IrCollectionKind.SortedDictionary] = "sortedDictionary",
            [IrCollectionKind.SortedList] = "sortedList",
        };

    /// <summary>
    /// The descriptor shape a stage 011h collection type classifies to;
    /// <c>null</c> for any other type.
    /// </summary>
    public static JsonPropertyKind? ShapeOf(INamedTypeSymbol type) =>
        KindOf(type) switch
        {
            null => null,
            var kind when IrCollectionKinds.IsKeyed(kind.Value) => JsonPropertyKind.Dictionary,
            var kind when IrCollectionKinds.IsSet(kind.Value) => JsonPropertyKind.HashSet,
            _ => JsonPropertyKind.List,
        };

    /// <summary>The wire kind of <paramref name="type"/>, or <c>null</c> for a type outside stage 011h.</summary>
    public static string? WireKindOf(ITypeSymbol type) =>
        KindOf(type) is { } kind ? WireKinds[kind] : null;

    /// <summary>
    /// The <c>create</c> closure of <paramref name="type"/>, or <c>null</c>
    /// for a type outside stage 011h.
    /// </summary>
    public static TsArrowFunction? CreateClosureOf(
        ITypeSymbol type,
        Func<TsExpression, ITypeSymbol, IrCollectionKind, TsExpression> withTypeArguments
    ) =>
        KindOf(type) switch
        {
            null => null,
            IrCollectionKind.ReadOnlyCollection => ThrowingClosure(type),
            IrCollectionKind.ImmutableDictionary or IrCollectionKind.IImmutableDictionary =>
                Closure("entries", parameter => new TsCallExpression(
                    new TsPropertyAccess(
                        withTypeArguments(
                            Factory(IrCollectionKind.ImmutableDictionary, "empty", []),
                            type,
                            IrCollectionKind.ImmutableDictionary
                        ),
                        "setItems"
                    ),
                    [parameter]
                )),
            IrCollectionKind.SortedSet => Closure("items", parameter =>
                withTypeArguments(
                    new TsNewExpression(
                        RuntimeCollections.ReferenceTo(IrCollectionKind.SortedSet),
                        [parameter]
                    ),
                    type,
                    IrCollectionKind.SortedSet
                )
            ),
            var kind when IrCollectionKinds.IsKeyed(kind.Value) => Closure("entries", parameter =>
                withTypeArguments(Factory(kind.Value, "assign", [parameter]), type, kind.Value)
            ),
            var kind => Closure("items", parameter =>
                withTypeArguments(
                    Factory(ClassOf(kind.Value), "from", [parameter]),
                    type,
                    ClassOf(kind.Value)
                )
            ),
        };

    private static IrCollectionKind? KindOf(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && CollectionKindResolver.KindOf(named) is { } kind
        && WireKinds.ContainsKey(kind)
            ? kind
            : null;

    /// <summary>The runtime class an interface kind reads into.</summary>
    private static IrCollectionKind ClassOf(IrCollectionKind kind) =>
        kind switch
        {
            IrCollectionKind.IImmutableList => IrCollectionKind.ImmutableList,
            IrCollectionKind.IImmutableSet => IrCollectionKind.ImmutableHashSet,
            _ => kind,
        };

    private static TsCallExpression Factory(
        IrCollectionKind kind,
        string factory,
        IReadOnlyList<TsExpression> arguments
    ) => new(new TsPropertyAccess(RuntimeCollections.ReferenceTo(kind), factory), arguments);

    private static TsArrowFunction Closure(
        string parameter,
        Func<TsExpression, TsExpression> body
    ) =>
        new(
            [new TsParameter(parameter, Type: null)],
            [new TsReturnStatement(body(new TsIdentifier(parameter)))]
        );

    /// <summary>
    /// <c>() =&gt; { throw new NotSupportedException(…) }</c>: System.Text.Json
    /// has no constructor to read a <c>ReadOnlyCollection&lt;T&gt;</c> through
    /// (its message, read from .NET 10, without the reader's position).
    /// </summary>
    private static TsArrowFunction ThrowingClosure(ITypeSymbol type) =>
        new(
            [new TsParameter("items", Type: null)],
            [
                new TsThrowStatement(
                    new TsNewExpression(
                        new TsRuntimeHelperReference("NotSupportedException", ExceptionsModule),
                        [
                            new TsStringLiteral(
                                "Deserialization of types without a parameterless constructor, a "
                                    + "singular parameterized constructor, or a parameterized "
                                    + "constructor annotated with 'JsonConstructorAttribute' is not "
                                    + $"supported. Type '{ClrTypeName.Of((INamedTypeSymbol)type)}'."
                            ),
                        ]
                    )
                ),
            ]
        );
}
