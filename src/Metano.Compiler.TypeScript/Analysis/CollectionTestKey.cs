using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>The tested collection definition and the static operand shape of one test.</summary>
internal readonly record struct CollectionTestKey
{
    /// <summary>The operand key of a native array.</summary>
    public const string ArrayOperand = "[]";

    /// <summary>
    /// The operand key of any static type that is neither an array nor a runtime
    /// collection: <c>object</c>, a type parameter, <c>IEnumerable&lt;T&gt;</c>, a class.
    /// </summary>
    public const string OpenOperand = "*";

    private CollectionTestKey(IrCollectionKind tested, string operand)
    {
        Tested = tested;
        Operand = operand;
    }

    public IrCollectionKind Tested { get; }
    public string Operand { get; }

    /// <summary>The operand key of a runtime collection kind; open for a kind with no CLR name.</summary>
    public static string OperandOfCollection(IrCollectionKind kind) =>
        kind is IrCollectionKind.KeyCollection or IrCollectionKind.ValueCollection
            ? kind.ToString()
            : RuntimeCollections.TryClrNameOf(kind) ?? OpenOperand;

    /// <summary>The operand key of the collection definition <paramref name="clrName"/>; open when it is not a runtime collection.</summary>
    public static string OperandOfClrName(string clrName) =>
        RuntimeCollections.KindOfClrName(clrName) is { } kind
            ? OperandOfCollection(kind)
            : OpenOperand;

    public static CollectionTestKey From(IrCollectionKind tested, string operand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operand);

        return new CollectionTestKey(tested, operand);
    }
}
