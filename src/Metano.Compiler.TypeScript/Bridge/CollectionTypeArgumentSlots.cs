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
    /// <summary>The runtime member a slot is appended to the call of.</summary>
    private enum Member
    {
        New,
        Of,
        From,
        WithCapacity,
        Assign,
        Adopt,
        ToList,
        ToHashSet,
        ToDictionary,
        Empty,
        Create,
        FromRange,
        FromArray,
        FromSelectors,
        AsReadOnly,
        ToImmutableList,
        ToImmutableArray,
        ToImmutableHashSet,
        ToImmutableDictionary,
    }

    private static readonly IReadOnlyDictionary<
        (IrCollectionKind Kind, Member Member),
        int
    > Positions = new Dictionary<(IrCollectionKind, Member), int>
    {
        [(IrCollectionKind.List, Member.New)] = 1,
        [(IrCollectionKind.List, Member.Of)] = 1,
        [(IrCollectionKind.List, Member.From)] = 1,
        [(IrCollectionKind.List, Member.WithCapacity)] = 1,
        [(IrCollectionKind.List, Member.Adopt)] = 2,
        [(IrCollectionKind.List, Member.ToList)] = 0,
        [(IrCollectionKind.Queue, Member.New)] = 1,
        [(IrCollectionKind.Queue, Member.From)] = 1,
        [(IrCollectionKind.Queue, Member.WithCapacity)] = 1,
        [(IrCollectionKind.Stack, Member.New)] = 1,
        [(IrCollectionKind.Stack, Member.From)] = 1,
        [(IrCollectionKind.Stack, Member.WithCapacity)] = 1,
        [(IrCollectionKind.HashSet, Member.New)] = 2,
        [(IrCollectionKind.HashSet, Member.Of)] = 2,
        [(IrCollectionKind.HashSet, Member.From)] = 2,
        [(IrCollectionKind.HashSet, Member.WithCapacity)] = 2,
        [(IrCollectionKind.HashSet, Member.ToHashSet)] = 0,
        [(IrCollectionKind.Dictionary, Member.New)] = 2,
        [(IrCollectionKind.Dictionary, Member.Of)] = 2,
        [(IrCollectionKind.Dictionary, Member.From)] = 2,
        [(IrCollectionKind.Dictionary, Member.Assign)] = 2,
        [(IrCollectionKind.Dictionary, Member.WithCapacity)] = 2,
        [(IrCollectionKind.Dictionary, Member.ToDictionary)] = 4,
        [(IrCollectionKind.ReadOnlyCollection, Member.New)] = 1,
        [(IrCollectionKind.ReadOnlyCollection, Member.Empty)] = 0,
        [(IrCollectionKind.ReadOnlyCollection, Member.FromArray)] = 1,
        [(IrCollectionKind.ReadOnlyCollection, Member.AsReadOnly)] = 1,
        [(IrCollectionKind.ImmutableList, Member.New)] = 2,
        [(IrCollectionKind.ImmutableList, Member.Empty)] = 0,
        [(IrCollectionKind.ImmutableList, Member.Of)] = 1,
        [(IrCollectionKind.ImmutableList, Member.From)] = 1,
        [(IrCollectionKind.ImmutableList, Member.ToImmutableList)] = 1,
        [(IrCollectionKind.ImmutableArray, Member.New)] = 2,
        [(IrCollectionKind.ImmutableArray, Member.Empty)] = 0,
        [(IrCollectionKind.ImmutableArray, Member.Of)] = 1,
        [(IrCollectionKind.ImmutableArray, Member.From)] = 1,
        [(IrCollectionKind.ImmutableArray, Member.FromRange)] = 3,
        [(IrCollectionKind.ImmutableArray, Member.ToImmutableArray)] = 1,
        [(IrCollectionKind.ImmutableHashSet, Member.New)] = 2,
        [(IrCollectionKind.ImmutableHashSet, Member.Empty)] = 0,
        [(IrCollectionKind.ImmutableHashSet, Member.Of)] = 2,
        [(IrCollectionKind.ImmutableHashSet, Member.From)] = 2,
        [(IrCollectionKind.ImmutableHashSet, Member.ToImmutableHashSet)] = 1,
        [(IrCollectionKind.ImmutableDictionary, Member.New)] = 3,
        [(IrCollectionKind.ImmutableDictionary, Member.Empty)] = 0,
        [(IrCollectionKind.ImmutableDictionary, Member.Create)] = 2,
        [(IrCollectionKind.ImmutableDictionary, Member.From)] = 3,
        [(IrCollectionKind.ImmutableDictionary, Member.FromSelectors)] = 5,
        [(IrCollectionKind.ImmutableDictionary, Member.ToImmutableDictionary)] = 1,
        [(IrCollectionKind.ImmutableListBuilder, Member.New)] = 1,
        [(IrCollectionKind.ImmutableArrayBuilder, Member.New)] = 1,
        [(IrCollectionKind.ImmutableHashSetBuilder, Member.New)] = 2,
        [(IrCollectionKind.ImmutableDictionaryBuilder, Member.New)] = 3,
        [(IrCollectionKind.SortedSet, Member.New)] = 2,
        [(IrCollectionKind.SortedSet, Member.Of)] = 2,
        [(IrCollectionKind.SortedSet, Member.From)] = 2,
        [(IrCollectionKind.SortedDictionary, Member.New)] = 2,
        [(IrCollectionKind.SortedDictionary, Member.Of)] = 2,
        [(IrCollectionKind.SortedDictionary, Member.From)] = 2,
        [(IrCollectionKind.SortedDictionary, Member.Assign)] = 2,
        [(IrCollectionKind.SortedList, Member.New)] = 2,
        [(IrCollectionKind.SortedList, Member.Of)] = 2,
        [(IrCollectionKind.SortedList, Member.From)] = 2,
        [(IrCollectionKind.SortedList, Member.Assign)] = 2,
        [(IrCollectionKind.SortedList, Member.WithCapacity)] = 2,
        [(IrCollectionKind.ConcurrentDictionary, Member.New)] = 2,
        [(IrCollectionKind.ConcurrentDictionary, Member.From)] = 2,
        [(IrCollectionKind.ConcurrentDictionary, Member.Assign)] = 2,
        [(IrCollectionKind.ConcurrentDictionary, Member.WithCapacity)] = 3,
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
                Arguments = Padded(created.Arguments, PositionOf(type.Kind, Member.New), slot),
            },
            TsCallExpression { Callee: TsPropertyAccess { Object: var owner } access } call
                when IsRuntimeClass(owner, type)
                    && MemberOfProperty(access.Property) is { } member => call with
            {
                Arguments = Padded(call.Arguments, PositionOf(type.Kind, member), slot),
            },
            TsCallExpression { Callee: TsRuntimeHelperReference { Name: var name } } call
                when MemberOfProperty(name) is { } member
                    && Positions.ContainsKey((type.Kind, member)) => call with
            {
                Arguments = Padded(call.Arguments, PositionOf(type.Kind, member), slot),
            },
            _ => construction,
        };
    }

    /// <summary>A declarative factory's bound result supplies its exact slot arguments.</summary>
    public static TsExpression AppendToMappedResult(
        TsExpression expression,
        IrCollectionTypeRef type,
        RuntimeTypeReachabilityResult reachability
    ) =>
        expression is TsTemplate template
            ? AppendToTemplate(template, type, reachability)
            : Append(expression, type, reachability);

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
                Arguments = Padded(call.Arguments, PositionOf(baseType.Kind, Member.New), slot),
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
            IrLinqOperator.ToList => Member.ToList,
            IrLinqOperator.ToSet => Member.ToHashSet,
            IrLinqOperator.ToMap => Member.ToDictionary,
            _ => (Member?)null,
        };

        if (member is null || SlotOf(type, reachability) is not { } slot)
            return stageCall;

        var position = PositionOf(type.Kind, member.Value);

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

        var callee = CalleeWithoutTypeArguments(text[..open]);
        var runtimeName = RuntimeCollections.NameOf(type.Kind);
        var member = MemberOfTemplate(text, open);

        if (
            !callee.StartsWith($"new {runtimeName}", StringComparison.Ordinal)
            && !callee.StartsWith($"{runtimeName}.", StringComparison.Ordinal)
            && !(callee == Spelling(member) && Positions.ContainsKey((type.Kind, member)))
        )
            return template;

        var count = TopLevelArgumentCount(text[(open + 1)..^1]);

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

    /// <summary>
    /// The member a mapped template calls: the one its callee text, the part before
    /// the parenthesis at <paramref name="open"/>, ends with
    /// (<c>List.of(</c>, <c>$0.toList(</c>), or <see cref="Member.New"/> when the
    /// callee names none (<c>new List&lt;$T0&gt;(</c>).
    /// </summary>
    private static Member MemberOfTemplate(string text, int open)
    {
        var callee = CalleeWithoutTypeArguments(text[..open]);

        return Enum.GetValues<Member>()
            .Where(member => member is not Member.New)
            .Cast<Member?>()
            .FirstOrDefault(member =>
                callee.EndsWith($".{Spelling(member!.Value)}") || callee == Spelling(member!.Value)
            )
            ?? Member.New;
    }

    private static Member? MemberOfProperty(string property) =>
        Enum.GetValues<Member>()
            .Cast<Member?>()
            .FirstOrDefault(member => Spelling(member!.Value) == property);

    private static string CalleeWithoutTypeArguments(string callee) =>
        callee.IndexOf('<') is var index && index >= 0 ? callee[..index] : callee;

    private static string Spelling(Member member)
    {
        var name = member.ToString();

        return $"{char.ToLowerInvariant(name[0])}{name[1..]}";
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
    ) => IrToTsRuntimeTypeReferences.SlotArgumentOf(argument, "type argument", reachability);

    private static bool IsRuntimeClass(TsExpression callee, IrCollectionTypeRef type) =>
        callee is TsRuntimeHelperReference { Name: var name }
        && name == RuntimeCollections.NameOf(type.Kind);

    /// <summary>
    /// The index of the <c>typeArguments</c> parameter: after the comparer of a
    /// dictionary or a set, after the options of <c>adopt</c>, else second. A kind
    /// with no row for the member is an internal error, never a guess.
    /// </summary>
    private static int PositionOf(IrCollectionKind kind, Member member) =>
        Positions.TryGetValue((kind, member), out var position)
            ? position
            : throw new InvalidOperationException(
                $"Internal error: no type-argument slot position for '{kind}.{Spelling(member)}'."
            );

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
