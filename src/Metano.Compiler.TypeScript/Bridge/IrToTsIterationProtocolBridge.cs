using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Bridges the C# enumeration contract to the JavaScript iteration protocol. A class
/// that implements <c>IEnumerable&lt;T&gt;</c> is declared <c>implements Iterable&lt;T&gt;</c>
/// and consumed by <c>for…of</c>, spread and the LINQ runtime, all of which call
/// <c>[Symbol.iterator]()</c> rather than <c>getEnumerator()</c>. The bridge adds
/// that member, delegating to the type's own <c>GetEnumerator()</c>:
/// <code>
/// [Symbol.iterator](): Iterator&lt;T&gt; {
///   return this.getEnumerator();
/// }
/// </code>
/// The call is virtual, so a derived class that overrides <c>GetEnumerator</c> is
/// iterated through its override.
/// </summary>
public static class IrToTsIterationProtocolBridge
{
    private const string IteratorMemberName = "[Symbol.iterator]";
    private const string EnumeratorMethodName = "GetEnumerator";

    /// <summary>
    /// The <c>[Symbol.iterator]()</c> member for <paramref name="ir"/>, or <c>null</c>
    /// when the type takes on no enumeration contract of its own
    /// (<see cref="IrTypeSemantics.EnumeratedElementType"/>) or declares no emitted
    /// parameterless instance <c>GetEnumerator()</c> to delegate to.
    /// </summary>
    public static TsMethodMember? BuildIteratorMember(
        IrClassDeclaration ir,
        IrToTsTypeOverrides? overrides
    )
    {
        if (ir.Semantics.EnumeratedElementType is not { } elementType)
            return null;

        if (FindEnumeratorMethod(ir) is not { } enumerator)
            return null;

        var enumeratorCall = new TsCallExpression(
            new TsPropertyAccess(
                new TsIdentifier("this"),
                IrToTsNamingPolicy.ToMethodName(enumerator.Name, enumerator.Attributes)
            ),
            []
        );

        return new TsMethodMember(
            IteratorMemberName,
            [],
            IrToTsTypeMapper.Map(new IrIteratorTypeRef(elementType), overrides),
            [new TsReturnStatement(enumeratorCall)]
        );
    }

    private static IrMethodDeclaration? FindEnumeratorMethod(IrClassDeclaration ir) =>
        ir
            .Members?.OfType<IrMethodDeclaration>()
            .FirstOrDefault(method =>
                method is { Name: EnumeratorMethodName, IsStatic: false, Parameters.Count: 0 }
            );
}
