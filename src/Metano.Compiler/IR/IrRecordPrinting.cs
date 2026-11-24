namespace Metano.Compiler.IR;

/// <summary>
/// Who supplies one of a record's text members (<c>ToString</c> or
/// <c>PrintMembers</c>), read from Roslyn (baseline FR-093).
/// </summary>
public enum IrRecordPrintFate
{
    /// <summary>The record has no such member of its own: a <c>sealed override ToString()</c> in a record base.</summary>
    None,

    /// <summary>C# synthesizes the member (Roslyn declares it implicitly).</summary>
    Synthesized,

    /// <summary>The source declares it.</summary>
    UserDeclared,
}

/// <summary>
/// The facts behind the .NET text of a record (baseline FR-093): its display
/// name, who supplies its <c>ToString</c> and <c>PrintMembers</c>, and the
/// members it prints at its own level. A target decides how to emit them
/// (FR-094); a record base's members are printed by the base, which a target
/// chains to.
/// </summary>
/// <param name="DisplayName">The C# simple name: no generic arity, no
/// containing type, no <c>[Name]</c> rename.</param>
/// <param name="ToStringFate">Who supplies the parameterless <c>ToString()</c>.</param>
/// <param name="PrintMembersFate">Who supplies <c>PrintMembers</c>: <see
/// cref="IrRecordPrintFate.UserDeclared"/> when the record or one of its record
/// bases declares it, which breaks the chain a target synthesizes.</param>
/// <param name="Members">The members the record prints at its own level, in
/// declaration order.</param>
public sealed record IrRecordPrinting(
    string DisplayName,
    IrRecordPrintFate ToStringFate,
    IrRecordPrintFate PrintMembersFate,
    IReadOnlyList<IrRecordPrintMember> Members
);

/// <summary>One printed member of a record: <c>Name = text</c>.</summary>
/// <param name="Name">The C# member name, printed as written.</param>
/// <param name="Text">The member's default text, exactly what the
/// interpolation hole <c>$"{this.Name}"</c> extracts to.</param>
public sealed record IrRecordPrintMember(string Name, IrExpression Text);
