namespace Metano.Compiler.IR;

/// <summary>
/// The declared member that implements one member of an enumeration contract,
/// under the name and attributes the type declares it with: an ordinary member keeps
/// its own, and an explicit implementation is declared under the interface member's
/// (see <c>ExplicitImplementationLowering</c>). The member may be inherited from a
/// base class. A backend applies its own naming policy to <see cref="Name"/>.
/// </summary>
public sealed record IrContractMember(string Name, IReadOnlyList<IrAttribute>? Attributes);

/// <summary>
/// The <c>IEnumerable&lt;T&gt;</c> contract (or the non-generic <c>IEnumerable</c>,
/// with an <c>object</c> element) a class or struct takes on itself rather than
/// inheriting it from its base type.
/// </summary>
/// <param name="ElementType">The enumerated element type.</param>
/// <param name="GetEnumerator">The member that implements <c>GetEnumerator()</c>.</param>
public sealed record IrEnumerableContract(IrTypeRef ElementType, IrContractMember GetEnumerator);

/// <summary>
/// The <c>IEnumerator&lt;T&gt;</c> contract (or the non-generic <c>IEnumerator</c>,
/// with an <c>object</c> element) a class or struct takes on itself rather than
/// inheriting it from its base type.
/// </summary>
/// <param name="ElementType">The element type <see cref="Current"/> yields.</param>
/// <param name="Current">The property that implements <c>Current</c>.</param>
/// <param name="MoveNext">The method that implements <c>MoveNext()</c>.</param>
public sealed record IrEnumeratorContract(
    IrTypeRef ElementType,
    IrContractMember Current,
    IrContractMember MoveNext
);
