/**
 * Late-bound dispatch table backing the `[StrictUnionGuard]` opt-in path
 * for sealed-hierarchy guards.
 *
 * Generated code for an abstract base with `[StrictUnionGuard]` looks
 * the variant guard up by discriminator value and delegates to it,
 * giving callers full shape validation without forcing the base file to
 * import every variant module (which would create an ESM cycle).
 *
 * Each variant module registers its guard at load time via
 * `registerUnionGuard("Circle", isCircle)`. If a discriminator value has
 * no entry — e.g., the variant module hasn't been loaded yet — the
 * generated base guard falls back to accepting the discriminator-only
 * narrow so the strict path is never more restrictive than the default.
 */
export const UnionGuardRegistry = new Map<string, (value: unknown) => boolean>();

/**
 * Registers a variant guard under its discriminator value. Idempotent:
 * a re-registration overwrites the previous entry, which lets HMR and
 * test fixtures replace a guard without resetting the whole map.
 */
export function registerUnionGuard(
  discriminator: string,
  guard: (value: unknown) => boolean,
): void {
  UnionGuardRegistry.set(discriminator, guard);
}

/**
 * Returns the variant guard registered for the given discriminator
 * value, or `undefined` when no variant module has registered for it.
 * Callers should treat the missing case as "fall back to a looser
 * narrow" — never as a hard failure — so the strict path degrades
 * gracefully when variant modules aren't loaded.
 */
export function getUnionGuard(discriminator: string): ((value: unknown) => boolean) | undefined {
  return UnionGuardRegistry.get(discriminator);
}
