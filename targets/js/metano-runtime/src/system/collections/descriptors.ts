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
import { Dictionary } from "./dictionary.ts";
import { HashSet } from "./hash-set.ts";
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

type InterfaceKind =
  | "IList"
  | "ICollection"
  | "IReadOnlyList"
  | "IReadOnlyCollection"
  | "IDictionary"
  | "IReadOnlyDictionary"
  | "ISet"
  | "IReadOnlySet";

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
};

let openInterfaces: Partial<Record<InterfaceKind, CollectionType<unknown>>> | undefined;

/** The open descriptor of a collection interface: a data literal, built on first use. */
function openInterface(kind: InterfaceKind): CollectionType<unknown> {
  openInterfaces ??= {};

  const known = openInterfaces[kind];

  if (known !== undefined) {
    return known;
  }

  const created: CollectionType<unknown> = {
    kind: "collection",
    collection: kind satisfies CollectionKindName,
    name: kind,
    fullName: `System.Collections.Generic.${kind}`,
    namespace: "System.Collections.Generic",
    typeArguments: [],
    variance: VARIANCE[kind],
    test: (_value: unknown): _value is unknown => true,
  };

  openInterfaces[kind] = created;

  return created;
}
