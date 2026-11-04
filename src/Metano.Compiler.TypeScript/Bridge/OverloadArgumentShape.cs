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
/// </summary>
internal sealed class OverloadArgumentShape
{
    private const string Arguments = "args";

    private readonly IReadOnlyList<IrParameter> _parameters;

    public OverloadArgumentShape(IReadOnlyList<IrParameter> parameters)
    {
        _parameters = parameters;
        Rest = parameters is [.., { IsParams: true } rest] ? rest : null;
        FixedCount = Rest is null ? parameters.Count : parameters.Count - 1;
        RequiredCount = parameters.Take(FixedCount).TakeWhile(p => !IsOptional(p)).Count();
    }

    /// <summary>The <c>params</c> parameter, when the overload declares one.</summary>
    public IrParameter? Rest { get; }

    /// <summary>The parameters before the <c>params</c> one.</summary>
    public int FixedCount { get; }

    /// <summary>The leading parameters a call must pass.</summary>
    public int RequiredCount { get; }

    /// <summary>Whether a call may omit some argument.</summary>
    public bool HasOptionalArguments => RequiredCount < FixedCount;

    /// <summary>
    /// Whether a call may omit the argument: the parameter has a C# default,
    /// or carries <c>[Optional]</c>.
    /// </summary>
    public static bool IsOptional(IrParameter parameter) =>
        parameter.HasDefaultValue || parameter.IsOptional;

    /// <summary>
    /// The runtime test that the dispatcher's <c>args</c> match this
    /// overload: the argument count, then each argument's type check.
    /// </summary>
    /// <param name="typeParameters">The type parameters in scope, innermost
    /// first (see <see cref="IrTypeCheckBuilder.Generate"/>).</param>
    public TsExpression Guard(IReadOnlyList<IrTypeParameter> typeParameters)
    {
        var fixedChecks = _parameters
            .Take(FixedCount)
            .Select((parameter, index) => FixedArgumentCheck(parameter, index, typeParameters));

        var clauses = new List<TsExpression?> { MinimumCountCheck(), MaximumCountCheck() };

        clauses.AddRange(fixedChecks);
        clauses.Add(RestArgumentsCheck(typeParameters));

        return IrTypeCheckBuilder.AllOf(clauses) ?? new TsLiteral("true");
    }

    /// <summary>
    /// The argument bound to the parameter at <paramref name="index"/>:
    /// <c>args[index]</c>, or the trailing <c>args.slice(index)</c> for the
    /// <c>params</c> parameter.
    /// </summary>
    public TsExpression ValueOf(int index) =>
        index == FixedCount && Rest is not null ? TrailingArguments() : ArgumentAt(index);

    /// <summary>
    /// The element type of a <c>params</c> parameter: <c>T</c> for
    /// <c>params T[]</c> (nullable or not); <c>null</c> for a <c>params</c>
    /// collection the IR models otherwise.
    /// </summary>
    private static IrTypeRef? ElementTypeOf(IrParameter rest) =>
        rest.Type switch
        {
            IrArrayTypeRef array => array.ElementType,
            IrNullableTypeRef { Inner: IrArrayTypeRef array } => array.ElementType,
            _ => null,
        };

    private static TsExpression ArgumentAt(int index) =>
        new TsElementAccess(new TsIdentifier(Arguments), new TsLiteral(index.ToString()));

    private static TsExpression ArgumentCount() =>
        new TsPropertyAccess(new TsIdentifier(Arguments), "length");

    private static TsExpression CountCheck(string comparison, int count) =>
        new TsBinaryExpression(ArgumentCount(), comparison, new TsLiteral(count.ToString()));

    /// <summary>
    /// <c>args.length === n</c> when every parameter is required, otherwise
    /// <c>args.length &gt;= required</c> (absent when nothing is required).
    /// </summary>
    private TsExpression? MinimumCountCheck()
    {
        if (Rest is null && !HasOptionalArguments)
            return CountCheck("===", FixedCount);

        return RequiredCount > 0 ? CountCheck(">=", RequiredCount) : null;
    }

    /// <summary>
    /// <c>args.length &lt;= n</c> for an overload with optional parameters and
    /// no <c>params</c> one, whose trailing argument count is unbounded.
    /// </summary>
    private TsExpression? MaximumCountCheck() =>
        Rest is null && HasOptionalArguments ? CountCheck("<=", FixedCount) : null;

    /// <summary>
    /// The type check of the argument at <paramref name="index"/>; an omitted
    /// optional argument (<c>undefined</c>) skips it.
    /// </summary>
    private static TsExpression? FixedArgumentCheck(
        IrParameter parameter,
        int index,
        IReadOnlyList<IrTypeParameter> typeParameters
    )
    {
        var argument = ArgumentAt(index);

        if (IrTypeCheckBuilder.Generate(parameter.Type, argument, typeParameters) is not { } check)
            return null;

        if (!IsOptional(parameter))
            return check;

        var omitted = new TsBinaryExpression(argument, "===", new TsIdentifier("undefined"));

        return new TsBinaryExpression(omitted, "||", check);
    }

    /// <summary>
    /// <c>args.slice(n).every((value) =&gt; check)</c> over the trailing
    /// arguments of a <c>params</c> parameter whose element type has a check.
    /// </summary>
    private TsExpression? RestArgumentsCheck(IReadOnlyList<IrTypeParameter> typeParameters)
    {
        if (Rest is null || ElementTypeOf(Rest) is not { } elementType)
            return null;

        var element = new TsIdentifier("value");

        if (IrTypeCheckBuilder.Generate(elementType, element, typeParameters) is not { } check)
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
            [new TsLiteral(FixedCount.ToString())]
        );
}
