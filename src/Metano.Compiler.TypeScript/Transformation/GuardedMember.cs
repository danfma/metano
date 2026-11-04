namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The coarse runtime test a generated shape guard applies to one member
/// (spec 009 FR-019 rule 3). <see cref="Present"/> (a presence-only check)
/// and <see cref="Unchecked"/> (a member the guard accepts whatever it holds,
/// absent included) accept any value, so they are compatible with every
/// kind; any other two kinds are compatible only when equal.
/// </summary>
internal enum GuardTestKind
{
    String,
    Number,
    Boolean,
    BigInt,
    Array,
    Object,
    Present,
    Unchecked,
}

/// <summary>
/// One member a generated shape guard checks: its TS name, the kind of its
/// test, whether the guard rejects the value when the member is absent (not
/// optional, not nullable, not unchecked), and the string literal it must
/// equal for a <c>[Discriminator]</c> member (<c>v.kind === "Circle"</c>).
/// </summary>
internal sealed record GuardedMember(
    string Name,
    GuardTestKind Kind,
    bool IsRequired,
    string? Literal = null
)
{
    /// <summary>
    /// Whether a value that passes <paramref name="other"/>'s test may pass
    /// this one: compatible kinds, and equal literals when both have one.
    /// </summary>
    public bool IsCompatibleWith(GuardedMember other)
    {
        var differentLiterals =
            Literal is not null && other.Literal is not null && Literal != other.Literal;

        var kindsCompatible =
            Kind == other.Kind || AcceptsAnyValue(Kind) || AcceptsAnyValue(other.Kind);

        return !differentLiterals && kindsCompatible;
    }

    private static bool AcceptsAnyValue(GuardTestKind kind) =>
        kind is GuardTestKind.Present or GuardTestKind.Unchecked;
}

/// <summary>
/// The test a shape guard emits for one member: the boolean expression and
/// the <see cref="GuardTestKind"/> it amounts to. <see cref="AcceptsAbsent"/>
/// is set when the expression also holds for a missing member.
/// </summary>
internal sealed record GuardFieldTest(
    AST.TsExpression Check,
    GuardTestKind Kind,
    bool AcceptsAbsent
);
