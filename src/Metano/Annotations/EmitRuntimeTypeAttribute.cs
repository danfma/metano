namespace Metano.Annotations;

/// <summary>
/// Forces the emission of a type's runtime type descriptor (spec 011
/// FR-053), exactly as if a runtime question (<c>is</c>, <c>as</c>, a cast,
/// <c>GetType()</c>, <c>typeof</c>) reached it; on the assembly, for every
/// public transpiled type. The descriptors it names (bases, interfaces) are
/// emitted too. What a descriptor looks like is each target's own decision. A
/// package library already emits the descriptor of every public type (spec
/// 011 D29), so the attribute matters for other projects and for non-public
/// types.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Interface
        | AttributeTargets.Assembly
)]
public sealed class EmitRuntimeTypeAttribute : Attribute;
