using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The class an operator's or conversion's statics are declared on: its
/// emitted name, which the instance forwarders call through, and the type
/// parameters a static of a generic type re-declares (TypeScript forbids a
/// static member from naming the class's own, TS2302; spec 013 FR-028 does the
/// same for factories). <c>null</c> type parameters for a non-generic type.
/// </summary>
internal sealed record TsOperatorOwner(
    string TypeName,
    IReadOnlyList<TsTypeParameter>? TypeParameters
);
