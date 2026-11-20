/**
 * Closes a runtime collection class's open descriptor over type arguments
 * (`List<String>`), memoized on the identity of the open literal and of each argument,
 * so `listType(StringType) === getRuntimeType(list)` (spec 011 FR-008, D30). Each
 * class's `$runtimeType` literal reaches it through its `close` member, so
 * `getRuntimeType` closes a collection without importing it: a program that asks
 * `GetType()` but never builds a collection carries none of this code, and a program
 * that uses only `List` pulls nothing from `system/reflection`. Internal.
 */
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { type MemoNode, memoNodeFor } from "../identity-memo.ts";

let memo: MemoNode<CollectionType<unknown>> | undefined;
let empties: MemoNode<object> | undefined;

/** Open destinations constrain only known arguments; reuse retains the source's precise slots. */
export function canReuseCollectionTypeArguments(
  source: readonly RuntimeType[] | undefined,
  destination: readonly RuntimeType[] | null | undefined,
): boolean {
  return (
    !source ||
    !destination ||
    (source.length === destination.length &&
      source.every(
        (argument, index) =>
          argument === destination[index] ||
          argument.kind === "unknown" ||
          destination[index]!.kind === "unknown",
      ))
  );
}

/** When comparer changes force a copy, unresolved destinations still retain known source slots. */
export function collectionCopyTypeArguments(
  source: readonly RuntimeType[] | undefined,
  destination: readonly RuntimeType[] | null | undefined,
): readonly RuntimeType[] | undefined {
  return (
    destination?.map((argument, index) =>
      argument.kind === "unknown" ? (source?.[index] ?? argument) : argument,
    ) ?? source
  );
}

/** A closed generic's empty singleton, keyed by the same descriptor identities as its slot. */
export function emptyCollectionFor<T extends object>(
  open: CollectionType<unknown>,
  typeArguments: readonly RuntimeType[],
  create: () => T,
): T {
  empties ??= {};

  const node = memoNodeFor(empties, [open, ...typeArguments]);
  node.value ??= create();

  // The open descriptor and argument identities uniquely name this closed collection type.
  return node.value as T;
}

/**
 * `open` closed over the `$typeArguments$<Class>` slot `instance` carries, or `open`
 * itself when it carries none (the wildcard of FR-008).
 */
export function closeCollectionFor<T>(
  open: CollectionType<T>,
  instance: object,
): CollectionType<T> {
  const slot = (instance as Record<string, ReadonlyArray<RuntimeType> | undefined>)[
    `$typeArguments$${open.collection}`
  ];

  return slot === undefined ? open : closeCollectionClass(open, slot);
}

/**
 * Class kinds are invariant, so the closed test is `instanceof` plus an identity compare
 * against the instance's `$typeArguments$<Class>` slot, an absent slot or `UnknownType`
 * on either side matching anything.
 */
export function closeCollectionClass<T>(
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
      open.ctor !== undefined &&
      value instanceof open.ctor &&
      slotMatches(
        (value as Record<string, ReadonlyArray<RuntimeType> | undefined>)[
          `$typeArguments$${open.collection}`
        ],
        typeArguments,
      ),
  } as CollectionType<unknown>;

  return node.value as CollectionType<T>;
}

function slotMatches(
  slot: ReadonlyArray<RuntimeType> | undefined,
  typeArguments: ReadonlyArray<RuntimeType>,
): boolean {
  return (
    slot === undefined ||
    typeArguments.every((expected, position) => {
      const actual = slot[position];

      return (
        actual === undefined ||
        actual === expected ||
        actual.kind === "unknown" ||
        expected.kind === "unknown"
      );
    })
  );
}
