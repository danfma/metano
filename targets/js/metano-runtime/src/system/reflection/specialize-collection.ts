import { type MemoNode, memoNodeFor } from "./identity-memo.ts";
import { isAssignableFrom } from "./is-assignable-from.ts";
import type {
  CollectionInterfaceEntry,
  CollectionKindName,
  CollectionType,
  RuntimeType,
} from "./runtime-type.ts";

let memo: MemoNode<CollectionType<unknown>> | undefined;

/**
 * A closed collection descriptor (`List<String>`) from an open one (a class's
 * `$runtimeType` literal or an interface literal), memoized on the identity of the
 * open descriptor and of each argument, so `listType(StringType) === listType(StringType)`
 * and `getRuntimeType` answers the same object (spec 011 FR-008, D30). Living in
 * reflection, it lets `getRuntimeType` specialize from an instance's
 * `$typeArguments$<Class>` slot without importing `system/collections`.
 */
export function specializeCollection<T>(
  open: CollectionType<T>,
  typeArguments: ReadonlyArray<RuntimeType>,
): CollectionType<T> {
  memo ??= {};

  const node = memoNodeFor(memo, [open, ...typeArguments]);

  node.value ??= {
    ...open,
    name: `${open.name}<${typeArguments.map((argument) => argument.name).join(",")}>`,
    fullName: `${open.fullName}<${typeArguments.map((argument) => argument.fullName).join(",")}>`,
    typeArguments,
    test: (value: unknown): value is T =>
      matchesCollection(value, open as CollectionType<unknown>, typeArguments),
  } as CollectionType<unknown>;

  return node.value as CollectionType<T>;
}

/**
 * A class kind: `instanceof` the class, then the instance's slot against the arguments.
 * An interface kind: nominal through the nearest own collection descriptor's
 * `interfaces` entries, then the slot mapped to the interface's arguments.
 */
function matchesCollection(
  value: unknown,
  open: CollectionType<unknown>,
  typeArguments: ReadonlyArray<RuntimeType>,
): boolean {
  if (open.ctor !== undefined) {
    return (
      value instanceof open.ctor &&
      argumentsMatch(
        slotOf(value, open.collection),
        typeArguments.map((_argument, position) => position),
        open,
        typeArguments,
      )
    );
  }

  const found = implementedEntry(value, open.collection);

  return (
    found !== undefined &&
    argumentsMatch(slotOf(value, found.owner), found.entry.arguments, open, typeArguments)
  );
}

function slotOf(
  value: unknown,
  collection: CollectionKindName,
): ReadonlyArray<RuntimeType> | undefined {
  return (value as Record<string, ReadonlyArray<RuntimeType> | undefined>)[
    `$typeArguments$${collection}`
  ];
}

/**
 * Each tested argument against the slot entry the mapping names: identity for an
 * invariant parameter, assignability for a covariant one; an absent slot, a `null`
 * mapping or `UnknownType` on either side matches anything (FR-008's wildcard).
 */
function argumentsMatch(
  slot: ReadonlyArray<RuntimeType> | undefined,
  mapping: ReadonlyArray<number> | null,
  open: CollectionType<unknown>,
  typeArguments: ReadonlyArray<RuntimeType>,
): boolean {
  if (slot === undefined || mapping === null) {
    return true;
  }

  return typeArguments.every((expected, position) => {
    const actual = slot[mapping[position] ?? -1];

    if (actual === undefined || actual.kind === "unknown" || expected.kind === "unknown") {
      return true;
    }

    return open.variance[position] === "covariant"
      ? isAssignableFrom(expected, actual)
      : actual === expected;
  });
}

interface ImplementedEntry {
  readonly owner: CollectionKindName;
  readonly entry: CollectionInterfaceEntry;
}

/** The `interfaces` entry naming `collection` on the nearest own collection descriptor. */
function implementedEntry(
  value: unknown,
  collection: CollectionKindName,
): ImplementedEntry | undefined {
  if (value === null || typeof value !== "object") {
    return undefined;
  }

  for (
    let current: unknown = (value as { readonly constructor?: unknown }).constructor;
    typeof current === "function";
    current = Object.getPrototypeOf(current)
  ) {
    const descriptor = Object.hasOwn(current, "$runtimeType")
      ? (current as unknown as { readonly $runtimeType: RuntimeType }).$runtimeType
      : undefined;

    if (descriptor?.kind === "collection") {
      const entry = descriptor.interfaces?.find((candidate) => candidate.collection === collection);

      return entry === undefined ? undefined : { owner: descriptor.collection, entry };
    }
  }

  return undefined;
}
