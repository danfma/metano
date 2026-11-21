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
import { ImmutableArray } from "./immutable-array.ts";
import { ImmutableDictionary } from "./immutable-dictionary.ts";
import { ImmutableHashSet } from "./immutable-hash-set.ts";
import type {
  IImmutableDictionary,
  IImmutableList,
  IImmutableSet,
} from "./immutable-interfaces.ts";
import { ImmutableList } from "./immutable-list.ts";
import { ImmutableListBuilder } from "./immutable-list-builder.ts";
import { ImmutableArrayBuilder } from "./immutable-array-builder.ts";
import { ImmutableHashSetBuilder } from "./immutable-hash-set-builder.ts";
import { ImmutableDictionaryBuilder } from "./immutable-dictionary-builder.ts";
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

/**
 * `open` closed over `typeArguments`, typed as the descriptor of `C`. The one cast of the
 * factories: type arguments are erased at run time, so one open descriptor (typed for
 * `unknown` arguments) describes `C` for every argument; the closed descriptor's `test`
 * is what tells two specializations apart.
 */
function closed<C>(
  open: RuntimeType<unknown>,
  typeArguments: ReadonlyArray<RuntimeType>,
): CollectionType<C> {
  if (open.kind !== "collection") {
    throw new TypeError("A collection factory requires a collection descriptor.");
  }

  return specializeCollection(open, typeArguments) as CollectionType<C>;
}

export function listType<T>(element: RuntimeType<T>): CollectionType<List<T>> {
  return closed<List<T>>(List.$runtimeType, [element]);
}

export function immutableListBuilderType<T>(
  element: RuntimeType<T>,
): CollectionType<ImmutableListBuilder<T>> {
  return closed<ImmutableListBuilder<T>>(ImmutableListBuilder.$runtimeType, [element]);
}

export function immutableArrayBuilderType<T>(
  element: RuntimeType<T>,
): CollectionType<ImmutableArrayBuilder<T>> {
  return closed<ImmutableArrayBuilder<T>>(ImmutableArrayBuilder.$runtimeType, [element]);
}

export function immutableHashSetBuilderType<T>(
  element: RuntimeType<T>,
): CollectionType<ImmutableHashSetBuilder<T>> {
  return closed<ImmutableHashSetBuilder<T>>(ImmutableHashSetBuilder.$runtimeType, [element]);
}

export function immutableDictionaryBuilderType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<ImmutableDictionaryBuilder<K, V>> {
  return closed<ImmutableDictionaryBuilder<K, V>>(ImmutableDictionaryBuilder.$runtimeType, [
    key,
    value,
  ]);
}

export function dictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<Dictionary<K, V>> {
  return closed<Dictionary<K, V>>(Dictionary.$runtimeType, [key, value]);
}

export function hashSetType<T>(element: RuntimeType<T>): CollectionType<HashSet<T>> {
  return closed<HashSet<T>>(HashSet.$runtimeType, [element]);
}

export function queueType<T>(element: RuntimeType<T>): CollectionType<Queue<T>> {
  return closed<Queue<T>>(Queue.$runtimeType, [element]);
}

export function stackType<T>(element: RuntimeType<T>): CollectionType<Stack<T>> {
  return closed<Stack<T>>(Stack.$runtimeType, [element]);
}

export function immutableListType<T>(element: RuntimeType<T>): CollectionType<ImmutableList<T>> {
  return closed<ImmutableList<T>>(ImmutableList.$runtimeType, [element]);
}

export function immutableArrayType<T>(element: RuntimeType<T>): CollectionType<ImmutableArray<T>> {
  return closed<ImmutableArray<T>>(ImmutableArray.$runtimeType, [element]);
}

export function immutableHashSetType<T>(
  element: RuntimeType<T>,
): CollectionType<ImmutableHashSet<T>> {
  return closed<ImmutableHashSet<T>>(ImmutableHashSet.$runtimeType, [element]);
}

export function immutableDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<ImmutableDictionary<K, V>> {
  return closed<ImmutableDictionary<K, V>>(ImmutableDictionary.$runtimeType, [key, value]);
}

export function readOnlyListType<T>(element: RuntimeType<T>): CollectionType<ReadOnlyList<T>> {
  return closed<ReadOnlyList<T>>(ReadOnlyList.$runtimeType, [element]);
}

export function concurrentDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<ConcurrentDictionary<K, V>> {
  return closed<ConcurrentDictionary<K, V>>(ConcurrentDictionary.$runtimeType, [key, value]);
}

export function sortedSetType<T>(element: RuntimeType<T>): CollectionType<SortedSet<T>> {
  return closed<SortedSet<T>>(SortedSet.$runtimeType, [element]);
}

export function sortedDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<SortedDictionary<K, V>> {
  return closed<SortedDictionary<K, V>>(SortedDictionary.$runtimeType, [key, value]);
}

export function sortedListType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<SortedList<K, V>> {
  return closed<SortedList<K, V>>(SortedList.$runtimeType, [key, value]);
}

export function iListType<T>(element: RuntimeType<T>): CollectionType<IList<T>> {
  return closed<IList<T>>(openInterface("IList"), [element]);
}

export function iCollectionType<T>(element: RuntimeType<T>): CollectionType<ICollection<T>> {
  return closed<ICollection<T>>(openInterface("ICollection"), [element]);
}

export function iReadOnlyListType<T>(element: RuntimeType<T>): CollectionType<IReadOnlyList<T>> {
  return closed<IReadOnlyList<T>>(openInterface("IReadOnlyList"), [element]);
}

export function iReadOnlyCollectionType<T>(
  element: RuntimeType<T>,
): CollectionType<IReadOnlyCollection<T>> {
  return closed<IReadOnlyCollection<T>>(openInterface("IReadOnlyCollection"), [element]);
}

export function iDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<IDictionary<K, V>> {
  return closed<IDictionary<K, V>>(openInterface("IDictionary"), [key, value]);
}

export function iReadOnlyDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<IReadOnlyDictionary<K, V>> {
  return closed<IReadOnlyDictionary<K, V>>(openInterface("IReadOnlyDictionary"), [key, value]);
}

export function iSetType<T>(element: RuntimeType<T>): CollectionType<ISet<T>> {
  return closed<ISet<T>>(openInterface("ISet"), [element]);
}

export function iReadOnlySetType<T>(element: RuntimeType<T>): CollectionType<IReadOnlySet<T>> {
  return closed<IReadOnlySet<T>>(openInterface("IReadOnlySet"), [element]);
}

export function iImmutableListType<T>(element: RuntimeType<T>): CollectionType<IImmutableList<T>> {
  return closed<IImmutableList<T>>(openInterface("IImmutableList"), [element]);
}

export function iImmutableSetType<T>(element: RuntimeType<T>): CollectionType<IImmutableSet<T>> {
  return closed<IImmutableSet<T>>(openInterface("IImmutableSet"), [element]);
}

export function iImmutableDictionaryType<K, V>(
  key: RuntimeType<K>,
  value: RuntimeType<V>,
): CollectionType<IImmutableDictionary<K, V>> {
  return closed<IImmutableDictionary<K, V>>(openInterface("IImmutableDictionary"), [key, value]);
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

const INTERFACE_BASES: Partial<
  Record<
    InterfaceKind,
    ReadonlyArray<{
      readonly collection: InterfaceKind;
      readonly arguments: ReadonlyArray<number> | null;
    }>
  >
> = {
  IList: [{ collection: "ICollection", arguments: [0] }],
  IReadOnlyList: [{ collection: "IReadOnlyCollection", arguments: [0] }],
  ISet: [{ collection: "ICollection", arguments: [0] }],
  IReadOnlySet: [{ collection: "IReadOnlyCollection", arguments: [0] }],
  IDictionary: [{ collection: "ICollection", arguments: null }],
  IReadOnlyDictionary: [{ collection: "IReadOnlyCollection", arguments: null }],
  IImmutableList: [
    { collection: "IReadOnlyList", arguments: [0] },
    { collection: "IReadOnlyCollection", arguments: [0] },
  ],
  IImmutableSet: [
    { collection: "IReadOnlySet", arguments: [0] },
    { collection: "IReadOnlyCollection", arguments: [0] },
  ],
  IImmutableDictionary: [
    { collection: "IReadOnlyDictionary", arguments: [0, 1] },
    { collection: "IReadOnlyCollection", arguments: null },
  ],
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
  const interfaces = INTERFACE_BASES[kind];
  const created: CollectionType<unknown> = {
    kind: "collection",
    collection: kind satisfies CollectionKindName,
    name: kind,
    fullName: `${namespace}.${kind}`,
    namespace,
    typeArguments: [],
    variance: VARIANCE[kind],
    ...(interfaces !== undefined ? { interfaces } : {}),
    test: (_value: unknown): _value is unknown => true,
  };

  openInterfaces[kind] = created;

  return created;
}
