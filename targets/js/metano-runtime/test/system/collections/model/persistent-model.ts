import { expect } from "bun:test";
import type { SeededRandom } from "./seeded-random.ts";

/**
 * The model-based check of the immutable classes' tip-append sharing (spec 011
 * FR-054, research R18, decision W4-11 condition 2). The driver keeps many live
 * versions, mutates the newest one and older ones (branching), and after every
 * step compares EVERY live version with a plain-copy reference: count,
 * contents in enumeration order, membership and lookups over the whole value
 * domain, and whether an unchanged result is the receiver itself.
 *
 * A subject adapts one immutable class (an opaque version handle `V`); the
 * reference is the obvious copy-everything implementation of the same
 * contract, written here once per collection shape.
 */

/** The values the steps draw from: small, so duplicates and absent values are common. */
export const MODEL_DOMAIN = 12;

/** The `default(T)` the driver passes to `tryGetValue`: outside the domain, so a miss is visible. */
const MISSING = -1;

export type ModelShape = "list" | "set" | "dictionary";

/** One step, drawn by the driver and applied to both subject and reference. */
export type ModelStep =
  | { readonly op: "add"; readonly value: number; readonly item: number }
  | { readonly op: "addRange"; readonly items: readonly number[] }
  | { readonly op: "remove"; readonly value: number }
  | { readonly op: "setItem"; readonly value: number; readonly item: number }
  | { readonly op: "clear" };

/** What a subject must expose; dictionaries map `value` (the key) to `item`. */
export interface ModelSubject<V> {
  readonly name: string;
  readonly shape: ModelShape;
  empty(): V;
  apply(version: V, step: ModelStep): V;
  count(version: V): number;
  /** Elements (list, set) or `[key, value]` pairs (dictionary) in enumeration order. */
  contents(version: V): unknown[];
  /** Element membership (list, set) or key membership (dictionary). */
  contains(version: V, value: number): boolean;
  /**
   * `TryGetValue` (set: the stored element; dictionary: the value); absent
   * from lists. On a miss it returns `missing`, the `default(T)` the caller
   * passes (D30).
   */
  tryGetValue?(version: V, value: number, missing: number): readonly [boolean, unknown];
}

/** The plain-copy reference state of one version. */
type Reference = { readonly items: readonly number[]; readonly pairs: readonly (readonly [number, number])[] };

/**
 * `unchanged` marks the results .NET returns as the receiver itself (read from
 * .NET 10: Remove of an absent value, Add of a present set element, Add or
 * SetItem of an equal dictionary value); Clear and the range operations make
 * no identity promise here.
 */
type Outcome = { readonly next: Reference; readonly unchanged: boolean } | { readonly error: string };

const EMPTY: Reference = { items: [], pairs: [] };

export interface ModelRunOptions {
  readonly steps: number;
  /** The most versions kept live at once. */
  readonly maxVersions: number;
}

/**
 * Runs `options.steps` random steps of `subject` against the reference and
 * fails (through `expect`) at the first divergence, naming seed and step.
 */
export function runPersistentModel<V>(
  subject: ModelSubject<V>,
  random: SeededRandom,
  options: ModelRunOptions,
): void {
  const versions: { subject: V; reference: Reference }[] = [{ subject: subject.empty(), reference: EMPTY }];

  for (let step = 0; step < options.steps; step++) {
    const index = random.next() < 0.6 ? versions.length - 1 : random.below(versions.length);
    const source = versions[index] as { subject: V; reference: Reference };
    const drawn = drawStep(subject.shape, random, source.reference);
    const where = `${subject.name}, seed ${random.seed}, step ${step}: ${JSON.stringify(drawn)} on version ${index}`;
    const outcome = applyReference(subject.shape, source.reference, drawn);

    if ("error" in outcome) {
      expectThrows(() => subject.apply(source.subject, drawn), outcome.error, where);
    } else {
      const result = subject.apply(source.subject, drawn);

      if (outcome.unchanged) {
        expect(result === source.subject, `${where}: an unchanged result is the receiver`).toBe(true);
      }

      versions.push({ subject: result, reference: outcome.next });
    }

    if (versions.length > options.maxVersions) {
      versions.splice(random.below(versions.length - 1), 1);
    }

    for (const [live, version] of versions.entries()) {
      compareVersion(subject, version.subject, version.reference, `${where}, live version ${live}`);
    }
  }
}

function expectThrows(action: () => unknown, errorName: string, where: string): void {
  let thrown: unknown = null;

  try {
    action();
  } catch (error) {
    thrown = error;
  }

  expect(thrown instanceof Error ? thrown.name : thrown, `${where}: throws`).toBe(errorName);
}

function compareVersion<V>(subject: ModelSubject<V>, version: V, reference: Reference, where: string): void {
  const dictionary = subject.shape === "dictionary";

  expect(subject.count(version), `${where}: count`).toBe(dictionary ? reference.pairs.length : reference.items.length);
  expect(subject.contents(version), `${where}: contents`).toEqual(dictionary ? reference.pairs.map((pair) => [...pair]) : [...reference.items]);

  for (let value = 0; value < MODEL_DOMAIN; value++) {
    const pair = reference.pairs.find(([key]) => key === value);
    const present = dictionary ? pair !== undefined : reference.items.includes(value);

    expect(subject.contains(version, value), `${where}: contains(${value})`).toBe(present);

    if (subject.tryGetValue === undefined) {
      continue;
    }

    const expected = present ? [true, dictionary ? pair?.[1] : value] : [false, MISSING];

    expect([...subject.tryGetValue(version, value, MISSING)], `${where}: tryGetValue(${value})`).toEqual(expected);
  }
}

function drawStep(shape: ModelShape, random: SeededRandom, reference: Reference): ModelStep {
  const roll = random.next();
  const value = random.below(MODEL_DOMAIN);
  const item = random.below(3);

  if (roll < 0.45) {
    return { op: "add", value, item };
  }

  if (roll < 0.55) {
    const items = Array.from({ length: random.below(4) }, () => random.below(MODEL_DOMAIN));

    return { op: "addRange", items };
  }

  if (roll < 0.75) {
    return { op: "remove", value };
  }

  if (roll < 0.95) {
    const size = shape === "list" ? reference.items.length : MODEL_DOMAIN;

    return { op: "setItem", value: size === 0 ? 0 : random.below(size), item: shape === "list" ? random.below(MODEL_DOMAIN) : item };
  }

  return { op: "clear" };
}

/** The reference contract, per shape (.NET's observable behaviour, FR-054). */
function applyReference(shape: ModelShape, reference: Reference, step: ModelStep): Outcome {
  switch (shape) {
    case "list":
      return applyList(reference.items, step);
    case "set":
      return applySet(reference.items, step);
    case "dictionary":
      return applyDictionary(reference.pairs, step);
  }
}

function items(next: readonly number[], unchanged: boolean): Outcome {
  return { next: { items: next, pairs: [] }, unchanged };
}

function pairs(next: readonly (readonly [number, number])[], unchanged: boolean): Outcome {
  return { next: { items: [], pairs: next }, unchanged };
}

function applyList(current: readonly number[], step: ModelStep): Outcome {
  switch (step.op) {
    case "add":
      return items([...current, step.value], false);
    case "addRange":
      return items([...current, ...step.items], false);
    case "remove": {
      const index = current.indexOf(step.value);

      return index === -1 ? items(current, true) : items(current.filter((_, i) => i !== index), false);
    }

    case "setItem":
      if (step.value >= current.length) {
        return { error: "ArgumentOutOfRangeException" };
      }

      return items(current.map((existing, i) => (i === step.value ? step.item : existing)), false);
    case "clear":
      return items([], false);
  }
}

function applySet(current: readonly number[], step: ModelStep): Outcome {
  switch (step.op) {
    case "add":
    case "setItem":
      // A set has no SetItem: the step adds its element.
      return current.includes(step.value) ? items(current, true) : items([...current, step.value], false);
    case "addRange": {
      const next = [...current];

      for (const value of step.items) {
        if (!next.includes(value)) {
          next.push(value);
        }
      }

      return items(next, false);
    }

    case "remove":
      return current.includes(step.value) ? items(current.filter((value) => value !== step.value), false) : items(current, true);
    case "clear":
      return items([], false);
  }
}

function applyDictionary(current: readonly (readonly [number, number])[], step: ModelStep): Outcome {
  const index = "value" in step ? current.findIndex(([key]) => key === step.value) : -1;

  switch (step.op) {
    case "add": {
      if (index === -1) {
        return pairs([...current, [step.value, step.item]], false);
      }

      // .NET: an equal value returns the receiver, a different one throws.
      return current[index]?.[1] === step.item ? pairs(current, true) : { error: "ArgumentException" };
    }

    case "addRange": {
      // Exercised as SetItems (last wins): keys are the items, each mapped to 0.
      let next = current;

      for (const key of step.items) {
        const at = next.findIndex(([existing]) => existing === key);

        if (at === -1) {
          next = [...next, [key, 0]];
        } else if (next[at]?.[1] !== 0) {
          next = next.map((pair, i) => (i === at ? [key, 0] : pair));
        }
      }

      return pairs(next, false);
    }

    case "setItem":
      if (index === -1) {
        return pairs([...current, [step.value, step.item]], false);
      }

      if (current[index]?.[1] === step.item) {
        return pairs(current, true);
      }

      return pairs(current.map((pair, i) => (i === index ? [step.value, step.item] : pair)), false);
    case "remove":
      return index === -1 ? pairs(current, true) : pairs(current.filter((_, i) => i !== index), false);
    case "clear":
      return pairs([], false);
  }
}
