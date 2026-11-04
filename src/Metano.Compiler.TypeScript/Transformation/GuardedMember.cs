namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The coarse runtime test a generated shape guard applies to one member
/// (spec 009 FR-019 rule 3). Two kinds are compatible when they are equal or
/// either is <see cref="Present"/>, a presence-only check that accepts any
/// value. <see cref="Unchecked"/> marks a member the guard accepts whatever
/// it holds, absent included.
/// </summary>
public enum GuardTestKind
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
/// test, and whether the guard rejects the value when the member is absent
/// (not optional, not nullable, not unchecked).
/// </summary>
public sealed record GuardedMember(string Name, GuardTestKind Kind, bool IsRequired);

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
