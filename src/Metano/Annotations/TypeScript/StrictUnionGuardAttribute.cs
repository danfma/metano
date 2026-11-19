namespace Metano.Annotations.TypeScript;

/// <summary>
/// Opts a <c>[GenerateGuard]</c> abstract base into <em>strict</em> union
/// validation. Without this attribute the base's emitted <c>isT</c> guard
/// narrows on the discriminator field alone — fast, but only sound when
/// the caller trusts the wire shape. With <c>[StrictUnionGuard]</c> the
/// discriminator picks the variant, whose own guard then validates the
/// shape; an unknown discriminator is rejected.
///
/// <para>
/// The strict guard lives in its own module, <c>&lt;base-file&gt;.guard.ts</c>
/// (<c>shape.guard.ts</c> beside <c>shape.ts</c>), which imports the base and
/// every variant guard and holds a <c>variants</c> thunk table (spec 011
/// FR-029). The base module never imports a variant: a variant module extends
/// the base at load, so a base that imported it would be an ESM cycle. Nothing
/// registers at module load, so a bundle with <c>sideEffects: false</c> keeps
/// what it uses. A base whose variants are all nested in it declares them in
/// its own file, so its strict guard stays there.
/// </para>
///
/// <para>
/// This attribute is TypeScript-specific: it lives in the
/// <see cref="Metano.Annotations.TypeScript"/> namespace and is a no-op
/// outside the TS target.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
    Inherited = false
)]
public sealed class StrictUnionGuardAttribute : Attribute;
