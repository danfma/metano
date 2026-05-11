namespace Metano.Compiler.IR;

/// <summary>
/// Post-walk pass that hoists pure repeated sub-expressions out of an
/// <see cref="IrExprTreeNode"/> tree (#209). For every sub-tree that
/// (a) appears more than once by structural equality, (b) contains no
/// <see cref="IrExprCall"/> nodes (calls may carry side effects), and
/// (c) has at least <see cref="MinSizeToHoist"/> nodes, the pass
/// introduces a synthetic <see cref="IrExprLet"/> binding (named
/// <c>$0</c>, <c>$1</c>, …) and rewrites every previously-repeated
/// occurrence as an <see cref="IrExprRef"/>.
///
/// <para>
/// The canonicalizer uses an explicit
/// <see cref="StructuralComparer"/> rather than record value-equality
/// because <see cref="IrExprTreeNode"/> records carry
/// <see cref="IReadOnlyList{T}"/> children whose default equality is
/// reference-based — record-synthesized <c>Equals</c> would miss
/// structurally-identical clones produced by the walker.
/// </para>
///
/// <para>
/// Candidates are first collected by pre-order traversal of the tree
/// (so a larger qualifying parent subsumes its children) and then
/// emitted in <em>ascending-size</em> order — a smaller subtree can
/// never contain a larger one, so this guarantees a binding never
/// references a yet-to-be-declared <c>$N</c>. Within a size bucket
/// the pre-order position breaks ties (LINQ's stable
/// <c>OrderBy</c>), so the same input always emits the same
/// <c>$0</c>, <c>$1</c>, … sequence — a requirement for byte-stable
/// code generation.
/// </para>
/// </summary>
public static class IrExprTreeHoister
{
    /// <summary>
    /// Minimum node count a subtree must reach before it becomes a
    /// hoist candidate. Single-node leaves (<c>param</c>, <c>capture</c>,
    /// <c>literal</c>, <c>ref</c>) are too cheap to merit a binding —
    /// the rewrite would inflate the payload rather than shrink it.
    /// </summary>
    public const int MinSizeToHoist = 2;

    /// <summary>
    /// Threshold for the occurrence count before a subtree is hoisted.
    /// </summary>
    public const int MinOccurrencesToHoist = 2;

    /// <summary>
    /// Entry point. Returns the original <paramref name="tree"/> unchanged
    /// when no subtree qualifies; otherwise returns a single
    /// <see cref="IrExprLet"/> wrapping the rewritten body.
    /// </summary>
    public static IrExprTreeNode Hoist(IrExprTreeNode tree)
    {
        // Structural keys let us match clones the walker produced from
        // distinct Roslyn positions: same shape ⇒ same dictionary slot.
        var occurrences = new Dictionary<IrExprTreeNode, Occurrence>(StructuralComparer.Instance);
        Count(tree, occurrences);

        // Pre-order: a qualifying parent subsumes any nested qualifying
        // subtree, so the early return inside CollectCandidates keeps
        // the list to one entry per uniquely-shaped hoist root. The
        // structural HashSet keeps the second occurrence of an identical
        // subtree from re-entering the list.
        var candidates = new List<IrExprTreeNode>();
        var alreadyCollected = new HashSet<IrExprTreeNode>(StructuralComparer.Instance);
        CollectCandidates(tree, occurrences, candidates, alreadyCollected);

        if (candidates.Count == 0)
            return tree;

        // Emit smaller subtrees first so a later binding can reference an
        // earlier one — a strictly larger candidate may contain a smaller
        // candidate, and the runtime evaluator binds in declaration order.
        // Pre-order collection alone is not enough because the parent-side
        // early return means independent candidates (e.g. `(x.a.b) + 1`
        // and a separate `x.a.b` reached through a different branch) end
        // up unsorted with respect to dependency. Size-first ordering is
        // a simple, valid choice: a smaller subtree can never contain a
        // larger one structurally. OrderBy keeps a stable tie-breaker on
        // the pre-order position so naming stays deterministic for trees
        // that contain multiple same-size candidates.
        candidates = candidates.OrderBy(c => occurrences[c].Size).ToList();

        var nameByNode = new Dictionary<IrExprTreeNode, string>(StructuralComparer.Instance);
        for (var i = 0; i < candidates.Count; i++)
        {
            nameByNode[candidates[i]] =
                "$" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var bindings = new List<IrExprLetBinding>(candidates.Count);
        foreach (var candidate in candidates)
        {
            // The binding's *value* must not rewrite the candidate root
            // to a reference to itself — but nested earlier-qualifying
            // subtrees inside it still rewrite, keeping bindings shared.
            var value = Rewrite(candidate, nameByNode, skipRootRewrite: true);
            bindings.Add(new IrExprLetBinding(nameByNode[candidate], value));
        }

        var body = Rewrite(tree, nameByNode, skipRootRewrite: false);
        return new IrExprLet(bindings, body);
    }

    /// <summary>Per-subtree tally accumulated during the count pass.</summary>
    private sealed class Occurrence
    {
        public int Count;
        public int Size;
    }

    private static int Count(IrExprTreeNode node, Dictionary<IrExprTreeNode, Occurrence> counts)
    {
        if (counts.TryGetValue(node, out var existing))
        {
            existing.Count++;
            return existing.Size;
        }

        var size = 1;
        foreach (var child in Children(node))
            size += Count(child, counts);

        counts[node] = new Occurrence { Count = 1, Size = size };
        return size;
    }

    private static void CollectCandidates(
        IrExprTreeNode node,
        Dictionary<IrExprTreeNode, Occurrence> counts,
        List<IrExprTreeNode> candidates,
        HashSet<IrExprTreeNode> alreadyCollected
    )
    {
        var info = counts[node];
        if (info.Count >= MinOccurrencesToHoist && info.Size >= MinSizeToHoist && IsPure(node))
        {
            // A larger qualifying parent subsumes every nested
            // qualifying subtree: once we hoist the parent, the
            // children only occur once (inside the parent's binding)
            // and lose their repetition. Pre-order plus this early
            // return guarantees we never emit redundant inner
            // bindings for descendants of an already-hoisted node.
            if (alreadyCollected.Add(node))
                candidates.Add(node);
            return;
        }

        foreach (var child in Children(node))
            CollectCandidates(child, counts, candidates, alreadyCollected);
    }

    /// <summary>
    /// Pure = the subtree contains no <see cref="IrExprCall"/>,
    /// <see cref="IrExprNew"/>, or <see cref="IrExprLambda"/>.
    /// <list type="bullet">
    ///   <item><see cref="IrExprCall"/> may invoke user-defined code
    ///   with side effects.</item>
    ///   <item><see cref="IrExprNew"/> runs a constructor — providers
    ///   translating to SQL/OData rely on call-site identity and a
    ///   hoisted constructor would change the observable semantics.</item>
    ///   <item><see cref="IrExprLambda"/> introduces its own parameter
    ///   bindings; hoisting a subtree from inside a nested lambda to
    ///   the outer <c>let</c> would surface a free param reference
    ///   (the inner param is not in scope at the outer binding
    ///   site).</item>
    /// </list>
    /// Anything else (param / capture / literal / member / binary /
    /// unary / conditional / ref) is structurally pure.
    /// </summary>
    private static bool IsPure(IrExprTreeNode node)
    {
        if (node is IrExprCall or IrExprNew or IrExprLambda)
            return false;
        foreach (var child in Children(node))
        {
            if (!IsPure(child))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Rewrites <paramref name="node"/> by substituting any structurally
    /// equal subtree present in <paramref name="nameByNode"/> with an
    /// <see cref="IrExprRef"/>. <paramref name="skipRootRewrite"/>
    /// short-circuits the substitution at the root only — used when
    /// lowering a binding's own value so we don't fold the candidate
    /// to a self-reference.
    /// </summary>
    private static IrExprTreeNode Rewrite(
        IrExprTreeNode node,
        Dictionary<IrExprTreeNode, string> nameByNode,
        bool skipRootRewrite
    )
    {
        if (!skipRootRewrite && nameByNode.TryGetValue(node, out var name))
            return new IrExprRef(name);

        return node switch
        {
            IrExprParam or IrExprCapture or IrExprLiteral or IrExprRef => node,
            IrExprMember m => new IrExprMember(Rewrite(m.Target, nameByNode, false), m.Member),
            IrExprCall c => new IrExprCall(
                c.Target is null ? null : Rewrite(c.Target, nameByNode, false),
                c.Method,
                c.Args.Select(a => Rewrite(a, nameByNode, false)).ToList()
            ),
            IrExprBinary b => new IrExprBinary(
                b.Op,
                Rewrite(b.Left, nameByNode, false),
                Rewrite(b.Right, nameByNode, false)
            ),
            IrExprUnary u => new IrExprUnary(u.Op, Rewrite(u.Operand, nameByNode, false)),
            IrExprConditional cond => new IrExprConditional(
                Rewrite(cond.Condition, nameByNode, false),
                Rewrite(cond.WhenTrue, nameByNode, false),
                Rewrite(cond.WhenFalse, nameByNode, false)
            ),
            IrExprNew ne => new IrExprNew(
                ne.Type,
                ne.Args.Select(a => Rewrite(a, nameByNode, false)).ToList(),
                ne.Initializers?.Select(init => new IrExprNewInitializer(
                        init.Member,
                        Rewrite(init.Value, nameByNode, false)
                    ))
                    .ToList()
            ),
            IrExprLambda lambda => new IrExprLambda(
                lambda.Params,
                Rewrite(lambda.Body, nameByNode, false)
            ),
            IrExprLet let => new IrExprLet(
                let.Bindings.Select(b => new IrExprLetBinding(
                        b.Name,
                        Rewrite(b.Value, nameByNode, false)
                    ))
                    .ToList(),
                Rewrite(let.Body, nameByNode, false)
            ),
            _ => node,
        };
    }

    /// <summary>
    /// Enumerates the immediate child subtrees of <paramref name="node"/>
    /// — the only enumeration the counter, candidate collector, purity
    /// check, and structural comparer need.
    /// </summary>
    private static IEnumerable<IrExprTreeNode> Children(IrExprTreeNode node)
    {
        switch (node)
        {
            case IrExprParam:
            case IrExprCapture:
            case IrExprLiteral:
            case IrExprRef:
                yield break;
            case IrExprMember m:
                yield return m.Target;
                break;
            case IrExprCall c:
                if (c.Target is not null)
                    yield return c.Target;
                foreach (var arg in c.Args)
                    yield return arg;
                break;
            case IrExprBinary b:
                yield return b.Left;
                yield return b.Right;
                break;
            case IrExprUnary u:
                yield return u.Operand;
                break;
            case IrExprConditional cond:
                yield return cond.Condition;
                yield return cond.WhenTrue;
                yield return cond.WhenFalse;
                break;
            case IrExprNew ne:
                foreach (var arg in ne.Args)
                    yield return arg;
                if (ne.Initializers is { } inits)
                {
                    foreach (var init in inits)
                        yield return init.Value;
                }
                break;
            case IrExprLambda lambda:
                yield return lambda.Body;
                break;
            case IrExprLet let:
                foreach (var binding in let.Bindings)
                    yield return binding.Value;
                yield return let.Body;
                break;
        }
    }

    /// <summary>
    /// Structural equality + hashing for <see cref="IrExprTreeNode"/>.
    /// Necessary because the record-synthesized <c>Equals</c> compares
    /// <see cref="IReadOnlyList{T}"/> children by reference — two
    /// structurally identical subtrees produced from distinct Roslyn
    /// positions would not collide in a plain dictionary.
    /// </summary>
    private sealed class StructuralComparer : IEqualityComparer<IrExprTreeNode>
    {
        public static readonly StructuralComparer Instance = new();

        public bool Equals(IrExprTreeNode? x, IrExprTreeNode? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            if (x.GetType() != y.GetType())
                return false;

            // Type metadata on param/capture/literal/new is informational
            // (carried for the emitted descriptor but irrelevant to the
            // tree's identity). Two `x.Profile.Age` accesses are the same
            // hoist candidate even when the walker happened to attach
            // separately-allocated type refs to the leaves.
            return (x, y) switch
            {
                (IrExprParam a, IrExprParam b) => a.Name == b.Name,
                (IrExprCapture a, IrExprCapture b) => a.Name == b.Name,
                (IrExprLiteral a, IrExprLiteral b) => Equals(a.Value, b.Value),
                (IrExprMember a, IrExprMember b) => a.Member == b.Member
                    && Equals(a.Target, b.Target),
                (IrExprCall a, IrExprCall b) => a.Method == b.Method
                    && Equals(a.Target, b.Target)
                    && SequenceEquals(a.Args, b.Args),
                (IrExprBinary a, IrExprBinary b) => a.Op == b.Op
                    && Equals(a.Left, b.Left)
                    && Equals(a.Right, b.Right),
                (IrExprUnary a, IrExprUnary b) => a.Op == b.Op && Equals(a.Operand, b.Operand),
                (IrExprConditional a, IrExprConditional b) => Equals(a.Condition, b.Condition)
                    && Equals(a.WhenTrue, b.WhenTrue)
                    && Equals(a.WhenFalse, b.WhenFalse),
                (IrExprNew a, IrExprNew b) => SequenceEquals(a.Args, b.Args)
                    && InitializersEqual(a.Initializers, b.Initializers),
                (IrExprLambda a, IrExprLambda b) => LambdaParamsEqual(a.Params, b.Params)
                    && Equals(a.Body, b.Body),
                (IrExprLet a, IrExprLet b) => BindingsEqual(a.Bindings, b.Bindings)
                    && Equals(a.Body, b.Body),
                (IrExprRef a, IrExprRef b) => a.Name == b.Name,
                _ => false,
            };
        }

        public int GetHashCode(IrExprTreeNode obj)
        {
            var hash = new HashCode();
            hash.Add(obj.GetType());
            switch (obj)
            {
                case IrExprParam p:
                    hash.Add(p.Name);
                    break;
                case IrExprCapture c:
                    hash.Add(c.Name);
                    break;
                case IrExprLiteral l:
                    hash.Add(l.Value);
                    break;
                case IrExprMember m:
                    hash.Add(m.Member);
                    hash.Add(GetHashCode(m.Target));
                    break;
                case IrExprCall c:
                    hash.Add(c.Method);
                    if (c.Target is not null)
                        hash.Add(GetHashCode(c.Target));
                    foreach (var arg in c.Args)
                        hash.Add(GetHashCode(arg));
                    break;
                case IrExprBinary b:
                    hash.Add(b.Op);
                    hash.Add(GetHashCode(b.Left));
                    hash.Add(GetHashCode(b.Right));
                    break;
                case IrExprUnary u:
                    hash.Add(u.Op);
                    hash.Add(GetHashCode(u.Operand));
                    break;
                case IrExprConditional cond:
                    hash.Add(GetHashCode(cond.Condition));
                    hash.Add(GetHashCode(cond.WhenTrue));
                    hash.Add(GetHashCode(cond.WhenFalse));
                    break;
                case IrExprNew ne:
                    foreach (var arg in ne.Args)
                        hash.Add(GetHashCode(arg));
                    if (ne.Initializers is { } inits)
                    {
                        foreach (var init in inits)
                        {
                            hash.Add(init.Member);
                            hash.Add(GetHashCode(init.Value));
                        }
                    }
                    break;
                case IrExprLambda lambda:
                    foreach (var p in lambda.Params)
                        hash.Add(p.Name);
                    hash.Add(GetHashCode(lambda.Body));
                    break;
                case IrExprLet let:
                    foreach (var binding in let.Bindings)
                    {
                        hash.Add(binding.Name);
                        hash.Add(GetHashCode(binding.Value));
                    }
                    hash.Add(GetHashCode(let.Body));
                    break;
                case IrExprRef r:
                    hash.Add(r.Name);
                    break;
            }
            return hash.ToHashCode();
        }

        private bool SequenceEquals(
            IReadOnlyList<IrExprTreeNode> a,
            IReadOnlyList<IrExprTreeNode> b
        )
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (!Equals(a[i], b[i]))
                    return false;
            }
            return true;
        }

        private bool InitializersEqual(
            IReadOnlyList<IrExprNewInitializer>? a,
            IReadOnlyList<IrExprNewInitializer>? b
        )
        {
            if (a is null && b is null)
                return true;
            if (a is null || b is null)
                return false;
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].Member != b[i].Member || !Equals(a[i].Value, b[i].Value))
                    return false;
            }
            return true;
        }

        private static bool LambdaParamsEqual(
            IReadOnlyList<IrExprLambdaParam> a,
            IReadOnlyList<IrExprLambdaParam> b
        )
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                // Same rationale as the IrExprParam case: type metadata
                // is informational, only the name participates in
                // identity for hoisting purposes.
                if (a[i].Name != b[i].Name)
                    return false;
            }
            return true;
        }

        private bool BindingsEqual(
            IReadOnlyList<IrExprLetBinding> a,
            IReadOnlyList<IrExprLetBinding> b
        )
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].Name != b[i].Name || !Equals(a[i].Value, b[i].Value))
                    return false;
            }
            return true;
        }
    }
}
