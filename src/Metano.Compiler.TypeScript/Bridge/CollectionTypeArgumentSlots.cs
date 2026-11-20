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
    private static readonly IReadOnlyDictionary<
        (IrCollectionKind Kind, string Member),
        int
    > Positions = new Dictionary<(IrCollectionKind, string), int>
    {
        [(IrCollectionKind.List, "new")] = 1,
        [(IrCollectionKind.List, "of")] = 1,
        [(IrCollectionKind.List, "from")] = 1,
        [(IrCollectionKind.List, "withCapacity")] = 1,
        [(IrCollectionKind.List, "adopt")] = 2,
        [(IrCollectionKind.List, "toList")] = 0,
        [(IrCollectionKind.HashSet, "new")] = 2,
        [(IrCollectionKind.HashSet, "of")] = 2,
        [(IrCollectionKind.HashSet, "from")] = 2,
        [(IrCollectionKind.HashSet, "withCapacity")] = 2,
        [(IrCollectionKind.HashSet, "toHashSet")] = 0,
        [(IrCollectionKind.Dictionary, "new")] = 2,
        [(IrCollectionKind.Dictionary, "of")] = 2,
        [(IrCollectionKind.Dictionary, "from")] = 2,
        [(IrCollectionKind.Dictionary, "assign")] = 2,
        [(IrCollectionKind.Dictionary, "withCapacity")] = 2,
        [(IrCollectionKind.Dictionary, "toDictionary")] = 4,
    };

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
        var member = terminal switch
        {
            IrLinqOperator.ToList => "toList",
            IrLinqOperator.ToSet => "toHashSet",
            IrLinqOperator.ToMap => "toDictionary",
            _ => null,
        };

        if (member is null || SlotOf(type, reachability) is not { } slot)
            return stageCall;

        var position = PositionOf(type.Kind, member);

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

        var member = Positions
            .Keys.Select(key => key.Member)
            .FirstOrDefault(name => text[..open].EndsWith($".{name}"))
            ?? "new";

        var position = PositionOf(type.Kind, member);
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
    ) => IrToTsRuntimeTypeReferences.ArgumentOf(argument, "type argument", reachability);

    private static bool IsRuntimeClass(TsExpression callee, IrCollectionTypeRef type) =>
        callee is TsRuntimeHelperReference { Name: var name }
        && name == RuntimeCollections.NameOf(type.Kind);

    /// <summary>
    /// The index of the <c>typeArguments</c> parameter: after the comparer of a
    /// dictionary or a set, after the options of <c>adopt</c>, else second.
    /// </summary>
    private static int PositionOf(IrCollectionKind kind, string member) =>
        Positions.TryGetValue((kind, member), out var position)
            ? position
            : Positions[(IrCollectionKind.List, member)];

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
