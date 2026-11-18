namespace Metano.Annotations;

/// <summary>
/// Forces the emission of a type's runtime type descriptor (spec 011
/// FR-053): a lazy <c>$runtimeType</c> getter on a class, record or struct,
/// an exported <c>X$runtimeType</c> literal for an interface or an enum,
/// exactly as if a runtime question (<c>is</c>, <c>as</c>, a cast,
/// <c>GetType()</c>, <c>typeof</c>) reached it. On the assembly, it does the
/// same for every public transpiled type. The descriptors these name (bases,
/// interfaces) are emitted too; an unused descriptor is still shaken from a
/// bundle. An <c>[EmitPackage]</c> library already emits the descriptor of
/// every public type, so the attribute matters for other projects and for
/// non-public types.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Interface
        | AttributeTargets.Assembly
)]
public sealed class EmitRuntimeTypeAttribute : Attribute;
