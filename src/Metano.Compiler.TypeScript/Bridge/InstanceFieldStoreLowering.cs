using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Emits the instance fields of a class as type-only declarations
/// (<c>declare f: T;</c>) and stores each field exactly once on every
/// constructor path (#795, W10-1), at the path's <see cref="TsInstanceFieldStores"/>
/// point: first the promoted members, in parameter order, then every other
/// field in declaration order — its captured parameter, its C# initializer or
/// default, or <c>undefined!</c> when it has none, which keeps the value the
/// class field had. The own keys are therefore created in the order the class
/// fields created them, and the constructor body runs after the stores, as
/// C# runs initializers before the constructor body.
/// <para>
/// A class field would run the same stores through <c>[[Define]]</c> right
/// after <c>super(...)</c>, which V8 runs about twice as slow in a derived
/// class, and a field the constructor assigns would be stored twice. C# runs
/// initializers before the base constructor; JavaScript cannot touch
/// <c>this</c> before <c>super(...)</c> returns, as before.
/// </para>
/// <para>
/// The trailing fields every constructor path assigns first, in declaration
/// order, at the start of its body (<see cref="InstanceFieldStorePlan"/>,
/// #787 N11) store nothing more: that body store is their one store. This
/// includes a member an explicit constructor promotes, when every field after
/// it is replaced too, so its key keeps its place.
/// </para>
/// <para>
/// Left alone: static and abstract members, members that already are
/// declarations, <c>override</c> fields (TypeScript rejects
/// <c>declare override</c>, TS1243), and <c>#private</c> fields, which a
/// <c>declare</c> cannot introduce. A class whose constructor has no store
/// point (the stub of an unsupported constructor) keeps its class fields.
/// </para>
/// </summary>
internal static class InstanceFieldStoreLowering
{
    public static TsClassBody Apply(TsClassBody body, InstanceFieldStorePlan plan)
    {
        var fields = body.Members.OfType<TsFieldMember>().Where(IsInstanceStorage).ToList();
        var replaced = ReplacedByTheBody(fields, plan);
        var lowered = new StoreRewriter(fields, replaced).Rewrite(body.Constructor.Body);

        if (lowered is null)
            return body;

        var members = body
            .Members.Select(member =>
                member is TsFieldMember field && IsInstanceStorage(field)
                    ? AsDeclaration(field)
                    : member
            )
            .ToList();

        return new TsClassBody(body.Constructor with { Body = lowered }, members);
    }

    private static bool IsInstanceStorage(TsFieldMember field) =>
        field is { Static: false, Abstract: false, Declare: false, Override: false }
        && !field.Name.StartsWith('#');

    /// <summary>
    /// The declaration-only field: no initializer and no definite-assignment
    /// assertion, which TypeScript rejects on a <c>declare</c> field (TS1255).
    /// </summary>
    private static TsFieldMember AsDeclaration(TsFieldMember field) =>
        field with
        {
            Declare = true,
            Initializer = null,
            StoreInitializer = null,
            DefinitelyAssigned = false,
        };

    /// <summary>
    /// The longest run of trailing fields whose store the constructor bodies
    /// make instead: each may give up its store (<see cref="InstanceFieldStorePlan.Replaceable"/>),
    /// and every constructor path assigns exactly these, in declaration order,
    /// before anything else reads the instance — so the keys keep their order.
    /// </summary>
    private static IReadOnlySet<string> ReplacedByTheBody(
        IReadOnlyList<TsFieldMember> fields,
        InstanceFieldStorePlan plan
    )
    {
        if (plan.LeadingRuns.Count == 0)
            return new HashSet<string>();

        var names = fields.Select(field => field.Name).ToList();

        for (var count = names.Count; count > 0; count--)
        {
            var trailing = names.Skip(names.Count - count).ToList();

            if (
                trailing.All(plan.Replaceable.Contains)
                && plan.LeadingRuns.All(run => AssignsInOrder(run, trailing))
            )
                return trailing.ToHashSet(StringComparer.Ordinal);
        }

        return new HashSet<string>();
    }

    private static bool AssignsInOrder(IReadOnlyList<string> run, IReadOnlyList<string> trailing) =>
        run.Where(trailing.Contains).SequenceEqual(trailing);

    /// <summary>
    /// Replaces every store point of a constructor body, descending into the
    /// branches and blocks a dispatcher lowers its overloads and inlined
    /// chains to.
    /// </summary>
    private sealed class StoreRewriter(
        IReadOnlyList<TsFieldMember> fields,
        IReadOnlySet<string> replacedByTheBody
    )
    {
        /// <summary>The rewritten statements; <c>null</c> when they hold no store point.</summary>
        public IReadOnlyList<TsStatement>? Rewrite(IReadOnlyList<TsStatement> statements)
        {
            var found = false;
            var rewritten = new List<TsStatement>(statements.Count);

            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case TsInstanceFieldStores point:
                        rewritten.AddRange(StoresAt(point));
                        found = true;
                        break;

                    default:
                        var nested = RewriteInside(statement);

                        found |= nested is not null;
                        rewritten.Add(nested ?? statement);
                        break;
                }
            }

            return found ? rewritten : null;
        }

        private TsStatement? RewriteInside(TsStatement statement)
        {
            switch (statement)
            {
                case TsBlockStatement block:
                    return Rewrite(block.Statements) is { } inner
                        ? block with
                        {
                            Statements = inner,
                        }
                        : null;

                case TsIfStatement branch:
                    var then = Rewrite(branch.Then);
                    var otherwise = branch.Else is { } elseBranch ? Rewrite(elseBranch) : null;

                    return then is null && otherwise is null
                        ? null
                        : branch with
                        {
                            Then = then ?? branch.Then,
                            Else = otherwise ?? branch.Else,
                        };

                default:
                    return null;
            }
        }

        private IEnumerable<TsStatement> StoresAt(TsInstanceFieldStores point)
        {
            var promoted = point
                .Promoted.Select(binding => binding.Member)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var binding in point.Promoted.Where(IsStoredHere))
                yield return Store(binding.Member, binding.Value);

            foreach (var field in fields)
            {
                if (promoted.Contains(field.Name) || replacedByTheBody.Contains(field.Name))
                    continue;

                yield return StoreOf(field, point);
            }
        }

        /// <summary>
        /// The store of a field at a store point. An initializer with a prelude gets a
        /// block that runs it, so its locals never clash with the constructor body's own.
        /// </summary>
        private static TsStatement StoreOf(TsFieldMember field, TsInstanceFieldStores point)
        {
            if (
                field.StoreInitializer is { } scoped
                && !point.Captured.Any(binding => binding.Member == field.Name)
            )
                return new TsBlockStatement([.. scoped.Prelude, Store(field.Name, scoped.Value)]);

            return Store(field.Name, ValueOf(field, point));
        }

        private bool IsStoredHere(TsFieldStoreBinding binding) =>
            !replacedByTheBody.Contains(binding.Member);

        private static TsExpression ValueOf(TsFieldMember field, TsInstanceFieldStores point) =>
            point.Captured.FirstOrDefault(binding => binding.Member == field.Name)?.Value
            ?? field.Initializer
            ?? new TsPostfixUnaryExpression(new TsIdentifier("undefined"), "!");

        private static TsStatement Store(string member, TsExpression value) =>
            new TsExpressionStatement(
                new TsBinaryExpression(
                    new TsPropertyAccess(new TsIdentifier("this"), member),
                    "=",
                    value
                )
            );
    }
}

/// <summary>
/// What lets a constructor body's own store replace a field's store
/// (#787 N11): the <see cref="LeadingRuns"/> — per declared constructor, the
/// fields its body assigns before anything can read the instance, in the
/// order of their first store (empty for one chaining with <c>: this(...)</c>;
/// none when the class declares no constructor or has a base class) — and the
/// <see cref="Replaceable"/> fields, whose store has no effect to keep (no
/// C# initializer, or a literal one).
/// </summary>
internal sealed record InstanceFieldStorePlan(
    IReadOnlyList<IReadOnlyList<string>> LeadingRuns,
    IReadOnlySet<string> Replaceable
)
{
    public static InstanceFieldStorePlan None { get; } = new([], new HashSet<string>());
}
