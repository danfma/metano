/**
 * The collection descriptor factories (spec 011 FR-017, D30;
 * contracts/runtime-collections.md § Descriptor factories): `typeof(List<string>)`
 * is `listType(StringType)`. Each closes an open descriptor through reflection's
 * memoized `specializeCollection`, so equal arguments give the same object. The
 * open interface descriptors are data literals here; the class ones are the
 * classes' own `$runtimeType`.
 */
import type {
  CollectionKindName,
  CollectionType,
  RuntimeType,
} from "../reflection/runtime-type.ts";
import { specializeCollection } from "../reflection/specialize-collection.ts";
import { ConcurrentDictionary } from "./concurrent-dictionary.ts";
import { Dictionary } from "./dictionary.ts";
import { HashSet } from "./hash-set.ts";
import { ImmutableDictionary } from "./immutable-dictionary.ts";
import { ImmutableHashSet } from "./immutable-hash-set.ts";
import type {
  IImmutableDictionary,
  IImmutableList,
  IImmutableSet,
} from "./immutable-interfaces.ts";
import { ImmutableList } from "./immutable-list.ts";
import type {
  ICollection,
  IDictionary,
  IList,
  IReadOnlyCollection,
  IReadOnlyDictionary,
  IReadOnlyList,
  IReadOnlySet,
  ISet,
} from "./interfaces.ts";
import { List } from "./list.ts";
import { Queue } from "./queue.ts";
import { ReadOnlyList } from "./read-only-list.ts";
import { SortedDictionary } from "./sorted-dictionary.ts";
import { SortedList } from "./sorted-list.ts";
import { SortedSet } from "./sorted-set.ts";
import { Stack } from "./stack.ts";

export function listType<T>(element: RuntimeType<T>): CollectionType<List<T>> {
  return specializeCollection(List.$runtimeType, [element]) as CollectionType<List<T>>;
}

export function dictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<Dictionary<K, V>> {
  return specializeCollection(Dictionary.$runtimeType, [key, value]) as CollectionType<
    Dictionary<K, V>
  >;
}

export function hashSetType<T>(element: RuntimeType<T>): CollectionType<HashSet<T>> {
  return specializeCollection(HashSet.$runtimeType, [element]) as CollectionType<HashSet<T>>;
}

export function queueType<T>(element: RuntimeType<T>): CollectionType<Queue<T>> {
  return specializeCollection(Queue.$runtimeType, [element]) as CollectionType<Queue<T>>;
}

export function stackType<T>(element: RuntimeType<T>): CollectionType<Stack<T>> {
  return specializeCollection(Stack.$runtimeType, [element]) as CollectionType<Stack<T>>;
}

export function immutableListType<T>(element: RuntimeType<T>): CollectionType<ImmutableList<T>> {
  return specializeCollection(ImmutableList.$runtimeType, [element]) as CollectionType<
    ImmutableList<T>
  >;
}

export function immutableHashSetType<T>(
  element: RuntimeType<T>,
): CollectionType<ImmutableHashSet<T>> {
  return specializeCollection(ImmutableHashSet.$runtimeType, [element]) as CollectionType<
    ImmutableHashSet<T>
  >;
}

export function immutableDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<ImmutableDictionary<K, V>> {
  return specializeCollection(ImmutableDictionary.$runtimeType, [key, value]) as CollectionType<
    ImmutableDictionary<K, V>
  >;
}

export function readOnlyListType<T>(element: RuntimeType<T>): CollectionType<ReadOnlyList<T>> {
  return specializeCollection(ReadOnlyList.$runtimeType, [element]) as CollectionType<
    ReadOnlyList<T>
  >;
}

export function concurrentDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<ConcurrentDictionary<K, V>> {
  return specializeCollection(ConcurrentDictionary.$runtimeType, [key, value]) as CollectionType<
    ConcurrentDictionary<K, V>
  >;
}

export function sortedSetType<T>(element: RuntimeType<T>): CollectionType<SortedSet<T>> {
  return specializeCollection(SortedSet.$runtimeType, [element]) as CollectionType<SortedSet<T>>;
}

export function sortedDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<SortedDictionary<K, V>> {
  return specializeCollection(SortedDictionary.$runtimeType, [key, value]) as CollectionType<
    SortedDictionary<K, V>
  >;
}

export function sortedListType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<SortedList<K, V>> {
  return specializeCollection(SortedList.$runtimeType, [key, value]) as CollectionType<
    SortedList<K, V>
  >;
}

export function iListType<T>(element: RuntimeType<T>): CollectionType<IList<T>> {
  return specializeCollection(openInterface("IList"), [element]) as CollectionType<IList<T>>;
}

export function iCollectionType<T>(element: RuntimeType<T>): CollectionType<ICollection<T>> {
  return specializeCollection(openInterface("ICollection"), [element]) as CollectionType<
    ICollection<T>
  >;
}

export function iReadOnlyListType<T>(element: RuntimeType<T>): CollectionType<IReadOnlyList<T>> {
  return specializeCollection(openInterface("IReadOnlyList"), [element]) as CollectionType<
    IReadOnlyList<T>
  >;
}

export function iReadOnlyCollectionType<T>(
  element: RuntimeType<T>,
): CollectionType<IReadOnlyCollection<T>> {
  return specializeCollection(openInterface("IReadOnlyCollection"), [element]) as CollectionType<
    IReadOnlyCollection<T>
  >;
}

export function iDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<IDictionary<K, V>> {
  return specializeCollection(openInterface("IDictionary"), [key, value]) as CollectionType<
    IDictionary<K, V>
  >;
}

export function iReadOnlyDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<IReadOnlyDictionary<K, V>> {
  return specializeCollection(openInterface("IReadOnlyDictionary"), [key, value]) as CollectionType<
    IReadOnlyDictionary<K, V>
  >;
}

export function iSetType<T>(element: RuntimeType<T>): CollectionType<ISet<T>> {
  return specializeCollection(openInterface("ISet"), [element]) as CollectionType<ISet<T>>;
}

export function iReadOnlySetType<T>(element: RuntimeType<T>): CollectionType<IReadOnlySet<T>> {
  return specializeCollection(openInterface("IReadOnlySet"), [element]) as CollectionType<
    IReadOnlySet<T>
  >;
}

export function iImmutableListType<T>(element: RuntimeType<T>): CollectionType<IImmutableList<T>> {
  return specializeCollection(openInterface("IImmutableList"), [element]) as CollectionType<
    IImmutableList<T>
  >;
}

export function iImmutableSetType<T>(element: RuntimeType<T>): CollectionType<IImmutableSet<T>> {
  return specializeCollection(openInterface("IImmutableSet"), [element]) as CollectionType<
    IImmutableSet<T>
  >;
}

export function iImmutableDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<IImmutableDictionary<K, V>> {
  return specializeCollection(openInterface("IImmutableDictionary"), [
    key,
    value,
  ]) as CollectionType<IImmutableDictionary<K, V>>;
}

type InterfaceKind =
  | "IList"
  | "ICollection"
  | "IReadOnlyList"
  | "IReadOnlyCollection"
  | "IDictionary"
  | "IReadOnlyDictionary"
  | "ISet"
  | "IReadOnlySet"
  | "IImmutableList"
  | "IImmutableSet"
  | "IImmutableDictionary";

/** Covariant (`out T`) parameters: the read-only list and collection interfaces. */
const VARIANCE: Readonly<Record<InterfaceKind, ReadonlyArray<"invariant" | "covariant">>> = {
  IList: ["invariant"],
  ICollection: ["invariant"],
  IReadOnlyList: ["covariant"],
  IReadOnlyCollection: ["covariant"],
  IDictionary: ["invariant", "invariant"],
  IReadOnlyDictionary: ["invariant", "invariant"],
  ISet: ["invariant"],
  IReadOnlySet: ["invariant"],
  IImmutableList: ["invariant"],
  IImmutableSet: ["invariant"],
  IImmutableDictionary: ["invariant", "invariant"],
};

let openInterfaces: Partial<Record<InterfaceKind, CollectionType<unknown>>> | undefined;

/** The open descriptor of a collection interface: a data literal, built on first use. */
function openInterface(kind: InterfaceKind): CollectionType<unknown> {
  openInterfaces ??= {};

  const known = openInterfaces[kind];

  if (known !== undefined) {
    return known;
  }

  const namespace = kind.startsWith("IImmutable")
    ? "System.Collections.Immutable"
    : "System.Collections.Generic";
  const created: CollectionType<unknown> = {
    kind: "collection",
    collection: kind satisfies CollectionKindName,
    name: kind,
    fullName: `${namespace}.${kind}`,
    namespace,
    typeArguments: [],
    variance: VARIANCE[kind],
    test: (_value: unknown): _value is unknown => true,
  };

  openInterfaces[kind] = created;

  return created;
}
