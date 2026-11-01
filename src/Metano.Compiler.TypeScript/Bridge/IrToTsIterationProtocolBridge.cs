using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Bridges the C# enumeration contracts to the JavaScript iteration protocol.
/// <list type="bullet">
///   <item>A class that implements <c>IEnumerable&lt;T&gt;</c> is declared
///     <c>implements Iterable&lt;T&gt;</c> and consumed by <c>for…of</c>, spread and
///     the LINQ runtime, all of which call <c>[Symbol.iterator]()</c> rather than
///     <c>getEnumerator()</c>. The bridge adds that member, delegating to the member
///     that implements <c>GetEnumerator()</c> (declared on the type or inherited):
///     <code>
///     [Symbol.iterator](): Iterator&lt;T&gt; {
///       return this.getEnumerator();
///     }
///     </code>
///     The call is virtual, so a derived class that overrides <c>GetEnumerator</c>
///     is iterated through its override.</item>
///   <item>A class that implements <c>IEnumerator&lt;T&gt;</c> is declared
///     <c>implements Iterator&lt;T&gt;</c>. The bridge adds <c>next()</c> over the
///     C# cursor:
///     <code>
///     next(): IteratorResult&lt;T&gt; {
///       return this.moveNext() ? { value: this.current, done: false } : { value: undefined, done: true };
///     }
///     </code>
///     When the type already declares a member emitted as <c>next</c>, that member
///     claims the name the protocol reserves: no <c>next()</c> is synthesized and the
///     class is not declared <c>implements Iterator&lt;T&gt;</c>, since the declared
///     member's signature need not match the protocol's
///     (see <see cref="DeclaresConflictingNext"/>).</item>
/// </list>
/// </summary>
public static class IrToTsIterationProtocolBridge
{
    private const string IteratorMemberLabel = "[Symbol.iterator]";
    private const string NextMethodName = "next";

    /// <summary>
    /// The iteration-protocol members <paramref name="ir"/> needs: none, or
    /// <c>[Symbol.iterator]()</c> and/or <c>next()</c> for the contracts it takes on
    /// itself (<see cref="IrTypeSemantics.Enumerable"/>,
    /// <see cref="IrTypeSemantics.Enumerator"/>).
    /// </summary>
    public static IEnumerable<TsMethodMember> BuildMembers(
        IrClassDeclaration ir,
        IrToTsTypeOverrides? overrides
    )
    {
        if (ir.Semantics.Enumerable is { } enumerable)
            yield return BuildIteratorMember(enumerable, overrides);

        if (ir.Semantics.Enumerator is { } enumerator && !DeclaresConflictingNext(ir))
            yield return BuildNextMember(enumerator, overrides);
    }

    /// <summary>
    /// <paramref name="ir"/> takes on <c>IEnumerator</c> but declares its own member
    /// emitted as <c>next</c>, so it cannot follow the JavaScript iterator protocol:
    /// it is neither given a synthesized <c>next()</c> nor declared
    /// <c>implements Iterator&lt;T&gt;</c>.
    /// </summary>
    public static bool DeclaresConflictingNext(IrClassDeclaration ir) =>
        ir.Semantics.Enumerator is not null && DeclaresNext(ir);

    /// <summary>
    /// The interfaces <paramref name="ir"/> is declared to implement: all of
    /// <see cref="IrClassDeclaration.Interfaces"/>, without the iterator contract
    /// when <see cref="DeclaresConflictingNext"/> holds (the declared <c>next</c> would
    /// fail the <c>Iterator&lt;T&gt;</c> check).
    /// </summary>
    public static IReadOnlyList<IrTypeRef>? ImplementedInterfaces(IrClassDeclaration ir) =>
        DeclaresConflictingNext(ir)
            ? ir.Interfaces?.Where(contract => contract is not IrIteratorTypeRef).ToList()
            : ir.Interfaces;

    private static TsMethodMember BuildIteratorMember(
        IrEnumerableContract enumerable,
        IrToTsTypeOverrides? overrides
    )
    {
        var enumeratorCall = new TsCallExpression(MethodOfThis(enumerable.GetEnumerator), []);

        return new TsMethodMember(
            IteratorMemberLabel,
            [],
            IrToTsTypeMapper.Map(new IrIteratorTypeRef(enumerable.ElementType), overrides),
            [new TsReturnStatement(enumeratorCall)],
            ComputedKey: new TsPropertyAccess(new TsIdentifier("Symbol"), "iterator")
        );
    }

    private static TsMethodMember BuildNextMember(
        IrEnumeratorContract enumerator,
        IrToTsTypeOverrides? overrides
    )
    {
        var current = new TsPropertyAccess(
            new TsIdentifier("this"),
            IrToTsNamingPolicy.ToDeclaredDataMemberName(
                enumerator.Current.Name,
                enumerator.Current.Attributes
            )
        );

        var step = new TsConditionalExpression(
            new TsCallExpression(MethodOfThis(enumerator.MoveNext), []),
            IteratorResult(current, done: false),
            IteratorResult(new TsIdentifier("undefined"), done: true)
        );

        var resultType = new TsNamedType(
            "IteratorResult",
            [IrToTsTypeMapper.Map(enumerator.ElementType, overrides)]
        );

        return new TsMethodMember(NextMethodName, [], resultType, [new TsReturnStatement(step)]);
    }

    private static TsObjectLiteral IteratorResult(TsExpression value, bool done) =>
        new([
            new TsObjectProperty("value", value),
            new TsObjectProperty("done", new TsLiteral(done ? "true" : "false")),
        ]);

    private static TsPropertyAccess MethodOfThis(IrContractMember method) =>
        new(
            new TsIdentifier("this"),
            IrToTsNamingPolicy.ToMethodName(method.Name, method.Attributes)
        );

    private static bool DeclaresNext(IrClassDeclaration ir) =>
        ir.Members?.Any(member => EmittedName(member) == NextMethodName) == true;

    private static string EmittedName(IrMemberDeclaration member) =>
        member is IrMethodDeclaration
            ? IrToTsNamingPolicy.ToMethodName(member.Name, member.Attributes)
            : IrToTsNamingPolicy.ToDeclaredDataMemberName(member.Name, member.Attributes);
}
