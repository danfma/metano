using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How one overload takes the arguments of a dispatcher declared
/// <c>(...args: unknown[])</c>, shared by the constructor and the method
/// dispatchers. A call site always passes a <c>params</c> array in its
/// expanded form (an explicit C# array is spread), and omits a trailing
/// argument whose parameter has a default, so an overload accepts:
/// <list type="bullet">
///   <item>between its required and its fixed parameter count, each
///   omitted (<c>undefined</c>) optional argument skipping its check;</item>
///   <item>with a <c>params</c> parameter, any number of trailing arguments,
///   each checked against the element type.</item>
/// </list>
/// A single array passed to a <c>params</c> parameter (C#'s normal form) is
/// deliberately not recognized: no call site emits it, and the rest-parameter
/// signature does not advertise it. A constructor's selector form (spec 013
/// FR-009) passes the overload's arguments after its selector
/// (<see cref="AfterSelector"/>).
/// </summary>
internal sealed class OverloadArgumentShape
{
    private const string Arguments = "args";

    private readonly IReadOnlyList<IrParameter> _parameters;
    private readonly int _firstArgumentIndex;
    private readonly int _declaredFixedCount;

    public OverloadArgumentShape(
        IReadOnlyList<IrParameter> parameters,
        IReadOnlyList<IrReifiedTypeParameter>? reifiedTypeParameters = null
    )
        : this(parameters, firstArgumentIndex: 0, reifiedTypeParameters) { }

    private OverloadArgumentShape(
        IReadOnlyList<IrParameter> parameters,
        int firstArgumentIndex,
        IReadOnlyList<IrReifiedTypeParameter>? reifiedTypeParameters = null
    )
    {
        _parameters = parameters;
        _firstArgumentIndex = firstArgumentIndex;
        Rest = parameters is [.., { IsRestParams: true } rest] ? rest : null;
        _declaredFixedCount = Rest is null ? parameters.Count : parameters.Count - 1;
        var reifiedCount = reifiedTypeParameters?.Count ?? 0;
        var reifiedOptional = reifiedTypeParameters?.Any(r => r.HasDefault) == true;
        FixedCount = _declaredFixedCount + reifiedCount;
        RequiredCount =
            reifiedCount > 0 && !reifiedOptional
                ? FixedCount
                : parameters.Take(_declaredFixedCount).TakeWhile(p => !p.CanBeOmitted).Count();
    }

    /// <summary>The <c>params</c> parameter, when the overload declares one.</summary>
    public IrParameter? Rest { get; }

    /// <summary>The parameters before the <c>params</c> one.</summary>
    public int FixedCount { get; }

    /// <summary>The leading parameters a call must pass.</summary>
    public int RequiredCount { get; }

    /// <summary>Whether a call may omit some argument.</summary>
    public bool HasOptionalArguments => RequiredCount < FixedCount;

    /// <summary>The most arguments the overload accepts.</summary>
    private int MaximumCount => Rest is null ? FixedCount : int.MaxValue;

    /// <summary>
    /// The overload's arguments as its selector form passes them, after the
    /// selector symbol at <c>args[0]</c>.
    /// </summary>
    public static OverloadArgumentShape AfterSelector(
        IReadOnlyList<IrParameter> parameters,
        IReadOnlyList<IrReifiedTypeParameter>? reifiedTypeParameters = null
    ) => new(parameters, firstArgumentIndex: 1, reifiedTypeParameters);

    /// <summary>
    /// The runtime test that the dispatcher's <c>args</c> match this
    /// overload: the argument count, then each argument's type check.
    /// </summary>
    /// <param name="scope">What the argument checks resolve against.</param>
    public TsExpression Guard(TypeCheckScope scope)
    {
        var fixedChecks = _parameters
            .Take(_declaredFixedCount)
            .Select(
                (parameter, index) =>
                    FixedArgumentCheck(
                        parameter,
                        ArgumentFor(index),
                        ScopeForArgument(index, parameter.Type, scope)
                    )
            );

        IReadOnlyList<TsExpression?> clauses =
        [
            .. ArgumentCountChecks(),
            .. fixedChecks,
            RestArgumentsCheck(scope),
        ];

        return IrTypeCheckBuilder.AllOf(clauses) ?? new TsLiteral("true");
    }

    /// <summary>
    /// The scope that checks the argument at <paramref name="index"/>: a
    /// delegate takes the length range that tells it from the delegates the
    /// overloads whose argument counts overlap this one's take there.
    /// </summary>
    private TypeCheckScope ScopeForArgument(int index, IrTypeRef type, TypeCheckScope scope)
    {
        if (
            DelegateArityRange.ArityOf(type) is not { } arity
            || scope.DispatchedParameterLists is not { } peers
        )
            return scope;

        var arities = peers
            .Select(parameters => new OverloadArgumentShape(parameters))
            .Where(OverlapsCounts)
            .Select(peer => peer.ArgumentTypeAt(index))
            .OfType<IrTypeRef>()
            .Select(DelegateArityRange.ArityOf)
            .OfType<int>();

        return scope with
        {
            DelegateArity = DelegateArityRange.Of(arity, arities),
        };
    }

    /// <summary>
    /// The type an argument at <paramref name="index"/> binds to: the declared
    /// parameter's type, or the element type of the <c>params</c> parameter for
    /// an index at or past it (a <c>params</c> delegate array takes delegates
    /// there, so its arity counts against the peers').
    /// </summary>
    private IrTypeRef? ArgumentTypeAt(int index) =>
        Rest is not null && index >= FixedCount
            ? Rest.ParamsElementType
            : _parameters.ElementAtOrDefault(index)?.Type;

    /// <summary>
    /// Whether some argument count both this overload and
    /// <paramref name="other"/> accept.
    /// </summary>
    public bool OverlapsCounts(OverloadArgumentShape other) =>
        Math.Max(RequiredCount, other.RequiredCount) <= Math.Min(MaximumCount, other.MaximumCount);

    /// <summary>
    /// The argument bound to the parameter at <paramref name="index"/>:
    /// <c>args[index]</c>, or the trailing <c>args.slice(index)</c> for the
    /// <c>params</c> parameter (each shifted past a selector).
    /// </summary>
    public TsExpression ValueOf(int index) =>
        index == FixedCount && Rest is not null ? TrailingArguments() : ArgumentFor(index);

    /// <summary>
    /// The argument-count checks: <c>args.length === n</c> when every
    /// parameter is required, otherwise <c>args.length &gt;= required</c>
    /// (when anything is required) and, without a <c>params</c> parameter,
    /// <c>args.length &lt;= n</c>.
    /// </summary>
    private IEnumerable<TsExpression> ArgumentCountChecks()
    {
        if (Rest is null && !HasOptionalArguments)
        {
            yield return CountCheck("===", FixedCount);

            yield break;
        }

        if (RequiredCount > 0)
            yield return CountCheck(">=", RequiredCount);

        if (Rest is null)
            yield return CountCheck("<=", FixedCount);
    }

    /// <summary>
    /// <c>args.slice(n).every((value) =&gt; check)</c> over the trailing
    /// arguments of a <c>params</c> parameter whose element type has a check.
    /// </summary>
    private TsExpression? RestArgumentsCheck(TypeCheckScope scope)
    {
        if (Rest?.ParamsElementType is not { } elementType)
            return null;

        var element = new TsIdentifier("value");

        var elementScope = ScopeForArgument(FixedCount, elementType, scope);

        if (IrTypeCheckBuilder.Generate(elementType, element, elementScope) is not { } check)
            return null;

        var elementCheck = new TsArrowFunction(
            [new TsParameter(element.Name, Type: null)],
            [new TsReturnStatement(check)]
        );

        return new TsCallExpression(
            new TsPropertyAccess(TrailingArguments(), "every"),
            [elementCheck]
        );
    }

    private TsExpression TrailingArguments() =>
        new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier(Arguments), "slice"),
            [new TsLiteral((_firstArgumentIndex + FixedCount).ToString())]
        );

    private TsExpression ArgumentFor(int index) => ArgumentAt(_firstArgumentIndex + index);

    /// <summary>The dispatcher's argument at <paramref name="index"/>: <c>args[index]</c>.</summary>
    internal static TsExpression ArgumentAt(int index) =>
        new TsElementAccess(new TsIdentifier(Arguments), new TsLiteral(index.ToString()));

    /// <summary>
    /// <c>argument === undefined</c>: the call omitted the argument, or passed
    /// <c>undefined</c> for it.
    /// </summary>
    internal static TsExpression IsOmitted(TsExpression argument) =>
        new TsBinaryExpression(argument, "===", new TsIdentifier("undefined"));

    private TsExpression CountCheck(string comparison, int count) =>
        new TsBinaryExpression(
            new TsPropertyAccess(new TsIdentifier(Arguments), "length"),
            comparison,
            new TsLiteral((_firstArgumentIndex + count).ToString())
        );

    /// <summary>
    /// The type check of <paramref name="argument"/>; an omitted
    /// optional argument (<c>undefined</c>) skips it.
    /// </summary>
    private static TsExpression? FixedArgumentCheck(
        IrParameter parameter,
        TsExpression argument,
        TypeCheckScope scope
    )
    {
        if (IrTypeCheckBuilder.Generate(parameter.Type, argument, scope) is not { } check)
            return null;

        return parameter.CanBeOmitted
            ? new TsBinaryExpression(IsOmitted(argument), "||", check)
            : check;
    }
}
