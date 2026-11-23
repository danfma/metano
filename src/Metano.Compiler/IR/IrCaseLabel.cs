namespace Metano.Compiler.IR;

/// <summary>
/// One label of a <c>switch</c> statement section (<see cref="IrSwitchCase"/>).
/// </summary>
public abstract record IrCaseLabel
{
    /// <summary>
    /// The label with <paramref name="rewrite"/> applied to the expressions it
    /// evaluates directly (a constant, a <c>when</c> guard), for IR rewriters.
    /// Like the rewriters' handling of <see cref="IrIsPatternExpression"/>,
    /// the constants inside a pattern are left as they are.
    /// </summary>
    public abstract IrCaseLabel MapExpressions(Func<IrExpression, IrExpression> rewrite);
}

/// <summary>
/// <c>case 1:</c>, <c>case "a":</c>, <c>case Color.Red:</c> — a constant the
/// subject is compared with.
/// </summary>
public sealed record IrConstantCaseLabel(IrExpression Value) : IrCaseLabel
{
    /// <summary>
    /// The static type of the tested value, when it may hold the constant only
    /// as a boxed enum (<c>object</c>, <c>ValueType</c>, <c>Enum</c>, an
    /// interface; spec 011 FR-050); <c>null</c> otherwise.
    /// </summary>
    public IrTypeRef? InputType { get; init; }

    /// <summary>The constant's enum type when <see cref="InputType"/> is set.</summary>
    public IrTypeRef? ConstantType { get; init; }

    /// <summary>
    /// Whether the constant's value is a floating-point NaN, however the source
    /// spells it (<c>double.NaN</c>, <c>0.0 / 0.0</c>, a <c>const</c> holding NaN).
    /// NaN never equals itself under IEEE comparison, while C# matches it.
    /// </summary>
    public bool IsNaN { get; init; }

    public override IrCaseLabel MapExpressions(Func<IrExpression, IrExpression> rewrite) =>
        this with
        {
            Value = rewrite(Value),
        };
}

/// <summary>
/// <c>case Circle c:</c>, <c>case &gt; 5:</c>, <c>case Square s when s.Side &gt; 2:</c>
/// — a pattern tested against the subject, optionally refined by a guard that
/// runs only once the pattern matched and may read its designators. The
/// designators are scoped to the section.
/// </summary>
public sealed record IrPatternCaseLabel(IrPattern Pattern, IrExpression? WhenClause = null)
    : IrCaseLabel
{
    public override IrCaseLabel MapExpressions(Func<IrExpression, IrExpression> rewrite) =>
        this with
        {
            WhenClause = WhenClause is null ? null : rewrite(WhenClause),
        };
}

/// <summary>
/// <c>default:</c> — taken when no other label of the switch matches, wherever
/// it appears.
/// </summary>
public sealed record IrDefaultCaseLabel : IrCaseLabel
{
    public override IrCaseLabel MapExpressions(Func<IrExpression, IrExpression> rewrite) => this;
}
