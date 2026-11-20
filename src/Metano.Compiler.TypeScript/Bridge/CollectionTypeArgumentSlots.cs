using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The trailing <c>typeArguments</c> a construction of a runtime collection
/// passes when the compilation asks a runtime question about its kind (spec 011
/// FR-008, D31): <c>new List&lt;number&gt;(null, [Int32Type])</c>,
/// <c>Dictionary.assign(entries, null, [StringType, UnknownType])</c>. Every
/// concrete argument has its descriptor (the reachability phase reached it); only
/// an open type parameter, or a type with no descriptor, reads <c>UnknownType</c>.
/// </summary>
internal static class CollectionTypeArgumentSlots
{
    /// <summary>
    /// <paramref name="construction"/> (a <c>new C(…)</c> or a <c>C.of / from / assign /
    /// withCapacity / adopt(…)</c> call) with the slot appended after its
    /// optional parameters, which a skipped comparer or options object pads
    /// with <c>null</c>; unchanged when the kind is not questioned.
    /// </summary>
    public static TsExpression Append(
        TsExpression construction,
        IrCollectionTypeRef type,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (SlotOf(type, reachability) is not { } slot)
            return construction;

        return construction switch
        {
            TsNewExpression created when IsRuntimeClass(created.Callee, type) => created with
            {
                Arguments = Padded(created.Arguments, PositionOf(type.Kind, "new"), slot),
            },
            TsCallExpression { Callee: TsPropertyAccess { Object: var owner } access } call
                when IsRuntimeClass(owner, type) => call with
            {
                Arguments = Padded(call.Arguments, PositionOf(type.Kind, access.Property), slot),
            },
            _ => construction,
        };
    }

    /// <summary>
    /// A class deriving from a runtime collection closes its arguments:
    /// <c>super(null, [Int32Type])</c> for <c>class IntList : List&lt;int&gt;</c>.
    /// </summary>
    public static TsBaseConstructorCall AppendToSuper(
        TsBaseConstructorCall call,
        IrCollectionTypeRef baseType,
        RuntimeTypeReachabilityResult reachability
    ) =>
        SlotOf(baseType, reachability) is { } slot
            ? call with
            {
                Arguments = Padded(call.Arguments, PositionOf(baseType.Kind, "new"), slot),
            }
            : call;

    /// <summary>
    /// The <c>toList</c> / <c>toDictionary</c> / <c>toHashSet</c> terminal call of a
    /// LINQ chain with the slot as its trailing argument, after the parameters the
    /// stage did not pass (<c>undefined</c>); any other stage is unchanged.
    /// </summary>
    public static TsExpression AppendToTerminal(
        TsExpression stageCall,
        IrLinqOperator terminal,
        IrCollectionTypeRef type,
        RuntimeTypeReachabilityResult reachability
    )
    {
        var position = terminal switch
        {
            IrLinqOperator.ToList or IrLinqOperator.ToSet => 0,
            IrLinqOperator.ToMap => 4,
            _ => -1,
        };

        if (position < 0 || SlotOf(type, reachability) is not { } slot)
            return stageCall;

        return stageCall is TsCallExpression call && call.Arguments.Count <= position
            ? call with
            {
                Arguments =
                [
                    .. call.Arguments,
                    .. Enumerable.Repeat<TsExpression>(
                        new TsLiteral("undefined"),
                        position - call.Arguments.Count
                    ),
                    slot,
                ],
            }
            : stageCall;
    }

    /// <summary>
    /// A mapped constructor template (<c>new List&lt;$T0&gt;()</c>, <c>Dictionary.from($0)</c>)
    /// with the slot as one more argument: the closing parenthesis is replaced
    /// by <c>, null, $n)</c> so the slot lands at the <c>typeArguments</c> position.
    /// A template whose text is not a single call is returned unchanged.
    /// </summary>
    public static TsTemplate AppendToTemplate(
        TsTemplate template,
        IrCollectionTypeRef type,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (SlotOf(type, reachability) is not { } slot || !template.Template.EndsWith(')'))
            return template;

        var text = template.Template;
        var open = MatchingOpenParenthesis(text);

        if (open < 0)
            return template;

        var count = TopLevelArgumentCount(text[(open + 1)..^1]);
        var position = PositionOf(type.Kind, text.Contains(".adopt(") ? "adopt" : "new");
        var nulls = string.Concat(Enumerable.Repeat("null, ", Math.Max(0, position - count)));
        var index = template.Arguments.Count;
        var lead = count > 0 ? ", " : "";

        return template with
        {
            Template = $"{text[..^1]}{lead}{nulls}${index})",
            Arguments = [.. template.Arguments, slot],
        };
    }

    private static int MatchingOpenParenthesis(string text)
    {
        var depth = 0;

        for (var index = text.Length - 1; index >= 0; index--)
        {
            depth += text[index] switch
            {
                ')' => 1,
                '(' => -1,
                _ => 0,
            };

            if (depth == 0)
                return index;
        }

        return -1;
    }

    private static int TopLevelArgumentCount(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return 0;

        var depth = 0;
        var count = 1;

        foreach (var character in arguments)
        {
            depth += character switch
            {
                '(' or '[' or '<' or '{' => 1,
                ')' or ']' or '>' or '}' => -1,
                _ => 0,
            };

            if (character == ',' && depth == 0)
                count++;
        }

        return count;
    }

    /// <summary>The <c>[Int32Type]</c> array of <paramref name="type"/>, <c>null</c> when none is passed.</summary>
    public static TsArrayLiteral? SlotOf(
        IrCollectionTypeRef type,
        RuntimeTypeReachabilityResult reachability
    ) =>
        !RuntimeCollections.IsInterface(type.Kind) && reachability.AsksAbout(type.Kind)
            ? new TsArrayLiteral([
                .. type.TypeArguments.Select(argument => Descriptor(argument, reachability)),
            ])
            : null;

    /// <summary>The descriptor of a type argument, <c>UnknownType</c> where it has none.</summary>
    public static TsExpression Descriptor(
        IrTypeRef argument,
        RuntimeTypeReachabilityResult reachability
    ) =>
        argument switch
        {
            IrCollectionTypeRef collection
                when RuntimeCollections.DescriptorFactoryOf(collection.Kind) is { } factory =>
                new TsCallExpression(
                    factory,
                    [.. collection.TypeArguments.Select(inner => Descriptor(inner, reachability))]
                ),
            IrNullableTypeRef nullable => new TsCallExpression(
                new TsRuntimeHelperReference("nullableType"),
                [Descriptor(nullable.Inner, reachability)]
            ),
            IrNamedTypeRef named when IsUnreachedClass(named, reachability) => Unknown(),
            _ => IrToTsRuntimeTypeReferences.Of(argument, "type argument", reachability)
                ?? Unknown(),
        };

    private static TsRuntimeHelperReference Unknown() => new("UnknownType");

    private static bool IsUnreachedClass(
        IrNamedTypeRef named,
        RuntimeTypeReachabilityResult reachability
    ) =>
        named.Origin is null
        && named.Semantics is { IsTranspilable: true }
        && !reachability.Reaches(named.SupertypeKey);

    private static bool IsRuntimeClass(TsExpression callee, IrCollectionTypeRef type) =>
        callee is TsRuntimeHelperReference { Name: var name }
        && name == RuntimeCollections.NameOf(type.Kind);

    /// <summary>
    /// The index of the <c>typeArguments</c> parameter: after the comparer of a
    /// dictionary or a set, after the options of <c>adopt</c>, else second.
    /// </summary>
    private static int PositionOf(IrCollectionKind kind, string member) =>
        member == "adopt" || kind is IrCollectionKind.Dictionary or IrCollectionKind.HashSet
            ? 2
            : 1;

    private static IReadOnlyList<TsExpression> Padded(
        IReadOnlyList<TsExpression> arguments,
        int position,
        TsExpression slot
    ) =>
        arguments.Count > position
            ? arguments
            :
            [
                .. arguments,
                .. Enumerable.Repeat<TsExpression>(
                    new TsLiteral("null"),
                    position - arguments.Count
                ),
                slot,
            ];
}
