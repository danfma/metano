import { ArgumentException } from "../exceptions/argument-exception.ts";
import { ArgumentOutOfRangeException } from "../exceptions/argument-out-of-range-exception.ts";
import { messageValue } from "../exceptions/message-value.ts";
import { NotSupportedException } from "../exceptions/not-supported-exception.ts";
import { throwArgumentNull } from "./throw-helpers.ts";

/**
 * The cold throw functions of the sorted classes (stage 011h) whose .NET
 * messages differ from 011c's `throw-helpers.ts` (probed on .NET 10). Each
 * returns `never` and stays out of the hot members (research R17). The
 * messages shared with 011c (`throwStackModified`'s enumerator text,
 * `throwArgumentNull`, `throwKeyNotFound`, `throwArrayTooSmall`) come from
 * `throw-helpers.ts`.
 */

const INDEX_OUT_OF_RANGE =
  "Index was out of range. Must be non-negative and less than the size of the collection.";

const COPY_TOO_SMALL =
  "Destination array is not long enough to copy all the items in the collection. Check array index and length.";

const SORTED_LIST_NESTED =
  "This operation is not supported on SortedList nested types because they require modifying the original SortedList.";

const DUPLICATE_KEY = "An item with the same key has already been added. Key:";

/** `SortedList`'s index members: the `List` text plus the actual value. */
export function throwIndexOutOfRange(index: number): never {
  throw new ArgumentOutOfRangeException("index", index, INDEX_OUT_OF_RANGE);
}

/** A negative `copyTo` index or `SortedList` capacity: `<name> ('<value>') must be a non-negative value.` */
export function throwNegativeArgument(paramName: string, value: number): never {
  const message = `${paramName} ('${messageValue(value)}') must be a non-negative value.`;

  throw new ArgumentOutOfRangeException(paramName, value, message);
}

/** `copyTo` of a sorted class into an array too short from the given index. */
export function throwCopyTooSmall(): never {
  throw new ArgumentException(COPY_TOO_SMALL);
}

/** `getViewBetween(lower, upper)` with `lower` above `upper`. */
export function throwViewBoundsInverted(): never {
  throw new ArgumentException("Must be less than or equal to upperValue.", "lowerValue");
}

/** A value outside a `SortedSet` view's range (`add`) or a nested view's bound outside its parent's. */
export function throwOutsideView(paramName: string): never {
  throw new ArgumentOutOfRangeException(paramName);
}

/** `SortedDictionary.add` of a present key: .NET formats the pair. */
export function throwDuplicatePair(key: unknown, value: unknown): never {
  throw new ArgumentException(`${DUPLICATE_KEY} [${messageValue(key)}, ${messageValue(value)}]`);
}

/** `SortedList.add` of a present key (with the `key` parameter) or a duplicate in its source (without). */
export function throwSortedListDuplicate(key: unknown, paramName: string | null): never {
  throw new ArgumentException(`${DUPLICATE_KEY} ${messageValue(key)}`, paramName);
}

/** A mutator of `SortedList.Keys` / `.Values` other than the key indexer write. */
export function throwSortedListNested(): never {
  throw new NotSupportedException(SORTED_LIST_NESTED);
}

/** `SortedList.Keys[i] = …`. */
export function throwKeyListWrite(): never {
  throw new NotSupportedException(
    "Mutating a key collection derived from a dictionary is not allowed.",
  );
}

/**
 * The `copyTo` validation the sorted classes share: `null` array, negative
 * index, then room for `count` elements.
 */
export function checkCopyTo(
  array: unknown[] | null | undefined,
  arrayIndex: number,
  count: number,
): void {
  if (array === null || array === undefined) {
    throwArgumentNull("array");
  }

  if (arrayIndex < 0) {
    throwNegativeArgument("index", arrayIndex);
  }

  if (array.length - arrayIndex < count) {
    throwCopyTooSmall();
  }
}
