using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The names a Dart call site resolves a selected overload to (spec 013
/// DECIDED-18): the named constructor of a constructor overload, the fast path
/// of a method overload; <c>null</c> when the declaring type is not visible,
/// which keeps the source spelling.
/// </summary>
public interface IDartOverloadNames
{
    string? ConstructorNameOf(IrOverloadRef overload);

    string? FastPathOf(IrOverloadRef overload);
}

/// <summary>The names of a rewrite that resolves no overload: every call site keeps its source spelling.</summary>
internal sealed class NoDartOverloadNames : IDartOverloadNames
{
    private NoDartOverloadNames() { }

    public static NoDartOverloadNames Instance { get; } = new();

    public string? ConstructorNameOf(IrOverloadRef overload) => null;

    public string? FastPathOf(IrOverloadRef overload) => null;
}
