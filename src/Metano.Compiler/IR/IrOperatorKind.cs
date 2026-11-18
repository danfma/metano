namespace Metano.Compiler.IR;

/// <summary>
/// The kind of a user-defined operator or conversion: its C# metadata name
/// without the <c>op_</c> prefix (<c>Addition</c>, <c>CheckedAddition</c>,
/// <c>UnaryNegation</c>, <c>Implicit</c>). The one spelling an operator's
/// declaration (<see cref="IrMethodSemantics.OperatorKind"/>) and its
/// applications (<see cref="IrUserOperator.Kind"/>) share; each target names
/// what it emits from it.
/// </summary>
public readonly record struct IrOperatorKind
{
    private const string MetadataPrefix = "op_";
    private const string CheckedPrefix = "Checked";

    private IrOperatorKind(string name) => Name = name;

    /// <summary>The kind's name (<c>Addition</c>).</summary>
    public string Name { get; }

    /// <summary>Whether the kind is a <c>checked</c> operator or conversion (<c>CheckedAddition</c>).</summary>
    public bool IsChecked => Name.StartsWith(CheckedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The kind's unchecked counterpart (<c>CheckedAddition</c> →
    /// <c>Addition</c>); the kind itself when it is not checked.
    /// </summary>
    public IrOperatorKind Unchecked =>
        IsChecked ? new IrOperatorKind(Name[CheckedPrefix.Length..]) : this;

    /// <summary>The kind of an operator whose metadata name is <paramref name="metadataName"/>.</summary>
    public static IrOperatorKind FromMetadataName(string metadataName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataName);

        return new IrOperatorKind(
            metadataName.StartsWith(MetadataPrefix, StringComparison.Ordinal)
                ? metadataName[MetadataPrefix.Length..]
                : metadataName
        );
    }

    public static implicit operator string(IrOperatorKind self) => self.Name;

    public override string ToString() => Name;
}
