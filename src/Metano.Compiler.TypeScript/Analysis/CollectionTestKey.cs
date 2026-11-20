using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>The tested collection definition and the static operand shape of one test.</summary>
internal readonly record struct CollectionTestKey
{
    private CollectionTestKey(IrCollectionKind tested, string operand)
    {
        Tested = tested;
        Operand = operand;
    }

    public IrCollectionKind Tested { get; }
    public string Operand { get; }

    public static CollectionTestKey From(IrCollectionKind tested, string operand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operand);

        return new CollectionTestKey(tested, operand);
    }
}
