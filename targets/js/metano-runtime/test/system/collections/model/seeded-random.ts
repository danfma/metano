/**
 * A small deterministic random source for the model-based collection tests
 * (mulberry32): the same seed always replays the same steps, so a failure
 * names the seed and step that reproduce it.
 */
export interface SeededRandom {
  readonly seed: number;
  /** A float in `[0, 1)`. */
  next(): number;
  /** An integer in `[0, bound)`. */
  below(bound: number): number;
  /** One element of a non-empty array. */
  pick<T>(items: readonly T[]): T;
}

export function createSeededRandom(seed: number): SeededRandom {
  let state = seed >>> 0;

  const next = (): number => {
    state = (state + 0x6d2b79f5) >>> 0;

    let mixed = state;

    mixed = Math.imul(mixed ^ (mixed >>> 15), mixed | 1);
    mixed ^= mixed + Math.imul(mixed ^ (mixed >>> 7), mixed | 61);

    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
  };

  const below = (bound: number): number => Math.floor(next() * bound);

  return {
    seed,
    next,
    below,
    pick: <T>(items: readonly T[]): T => items[below(items.length)] as T,
  };
}

/**
 * The seeds every model-based suite runs: a fixed set, plus the one in
 * `METANO_MODEL_SEED` when set (CI or a local reproduction).
 */
export function modelSeeds(): readonly number[] {
  const fixed = [1, 2, 3, 0xc0ffee];
  const extra = Number(process.env.METANO_MODEL_SEED);

  return Number.isInteger(extra) ? [...fixed, extra] : fixed;
}
