import type { JsonConverter } from "./types.ts";

/**
 * One entry per sequence of custom converters, keyed by the converters'
 * identities in order, not by the array that holds them: a fresh array of
 * the same converters per call (`serialize(v, spec, [money])`) shares the
 * entry, and an array changed in place reaches another one. No converters
 * and an empty array share the root entry, as both resolve no converter.
 * Every map is weak, so dropping a converter drops its entries.
 */
export interface ConverterSetCache<E> {
  entryFor(converters: readonly JsonConverter[] | undefined): E;
}

/** A trie node: the entry of the sequence that ends here, and the longer sequences. */
interface ConverterSetNode<E> {
  entry?: E;
  readonly next: WeakMap<JsonConverter, ConverterSetNode<E>>;
}

/**
 * `create` builds the entry of a sequence on first use; it receives its own
 * copy of the sequence, which later changes to the caller's array never reach.
 */
export function createConverterSetCache<E>(
  create: (converters: readonly JsonConverter[]) => E,
): ConverterSetCache<E> {
  const root = newNode<E>();

  return {
    entryFor(converters) {
      const sequence = converters ?? [];
      const node = sequence.reduce(childOf, root);

      return (node.entry ??= create(sequence.slice()));
    },
  };
}

function newNode<E>(): ConverterSetNode<E> {
  return { next: new WeakMap() };
}

function childOf<E>(node: ConverterSetNode<E>, converter: JsonConverter): ConverterSetNode<E> {
  const child = node.next.get(converter);

  if (child) {
    return child;
  }

  const created = newNode<E>();
  node.next.set(converter, created);

  return created;
}
