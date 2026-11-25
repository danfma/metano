/**
 * Throws `error` from an expression position: the tail of a lowered C#
 * `switch` expression no arm matched, or an arm whose result is a throw
 * expression (`_ => throw new X()`). Returning `never` lets the conditional
 * chain keep the type of its other arms, with no closure allocated.
 */
export function throwError(error: unknown): never {
  throw error;
}
