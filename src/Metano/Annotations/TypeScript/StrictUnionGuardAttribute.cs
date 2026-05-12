namespace Metano.Annotations.TypeScript;

/// <summary>
/// Opts a <c>[GenerateGuard]</c> abstract base into <em>strict</em> union
/// validation. Without this attribute the base's emitted <c>isT</c> guard
/// narrows on the discriminator field alone — fast, but only sound when
/// the caller trusts the wire shape. With <c>[StrictUnionGuard]</c> the
/// base's guard performs a per-variant shape check by looking the
/// discriminator value up in a shared runtime registry
/// (<c>UnionGuardRegistry</c>) and delegating to the registered variant
/// guard.
///
/// <para>
/// The registry is populated as a side-effect when each variant module
/// loads: the generated variant <c>.ts</c> emits a top-level
/// <c>registerUnionGuard("Circle", isCircle)</c> call. This avoids the
/// ESM import cycle that would arise if the abstract base file imported
/// every variant module directly: the base only imports the registry
/// (which has no dependency on the variants), so the value-import graph
/// stays acyclic. Variants still import the base for type/inheritance
/// purposes, which TypeScript handles without runtime ordering issues.
/// </para>
///
/// <para>
/// Fallback semantics: when no variant has registered a guard for the
/// observed discriminator (e.g., the variant module hasn't been loaded
/// yet, or the consumer only imported the base type), the strict guard
/// degrades gracefully to the discriminator-only check. The strict path
/// is therefore never <em>more</em> restrictive than the non-strict one
/// — opting in only tightens validation, never loosens it.
/// </para>
///
/// <para>
/// This attribute is TypeScript-specific. Dart and other targets have no
/// equivalent registry-based dispatch convention, so it lives in the
/// <see cref="Metano.Annotations.TypeScript"/> namespace and is treated
/// as a no-op outside the TS target.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
    Inherited = false
)]
public sealed class StrictUnionGuardAttribute : Attribute { }
