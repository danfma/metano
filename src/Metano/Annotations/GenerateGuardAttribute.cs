namespace Metano.Annotations;

/// <summary>
/// Generates a TypeScript type guard for this type. The emitted
/// <c>isT(value): value is T</c> predicate narrows on whatever
/// shape the type already carries — for plain classes and records
/// that is <c>instanceof</c> + property checks; for abstract bases
/// paired with <c>[StrictUnionGuard]</c> the dispatch routes through
/// the per-variant registry described in ADR-0023 so the base does
/// not value-import its subclasses (avoiding the ESM cycle).
/// A companion <c>assertT(value, message?)</c> throws on a failed
/// narrow.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Interface
)]
public sealed class GenerateGuardAttribute : Attribute;
