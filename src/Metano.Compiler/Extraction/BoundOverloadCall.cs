using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A call lowered to the overload Roslyn resolved: its bound arguments,
/// which are only valid together with that overload (spec 013 FR-017).
/// </summary>
internal sealed record BoundOverloadCall(
    IReadOnlyList<IrArgument> Arguments,
    IrSelectedOverload Overload
);
