/**
 * The cold throw functions of the runtime collections (spec 011 FR-016,
 * contracts/runtime-collections.md § Cold throw helpers). Each builds the
 * FR-013 exception with .NET's message; keeping them out of the hot members
 * keeps those small enough to inline (research R17).
 */
import { ArgumentException } from "../exceptions/argument-exception.ts";
import { ArgumentNullException } from "../exceptions/argument-null-exception.ts";
import { ArgumentOutOfRangeException } from "../exceptions/argument-out-of-range-exception.ts";
import { InvalidOperationException } from "../exceptions/invalid-operation-exception.ts";
import { KeyNotFoundException } from "../exceptions/key-not-found-exception.ts";
import { messageValue } from "../exceptions/message-value.ts";
import { NotSupportedException } from "../exceptions/not-supported-exception.ts";

/** A running enumeration saw its collection change (every enumerator but `Stack`'s). */
export function throwModified(): never {
  throw new InvalidOperationException(
    "Collection was modified; enumeration operation may not execute.",
  );
}

/** A `Stack<T>` (or, stage 011h, sorted-class) enumerator saw its collection change. */
export function throwEnumeratorModified(): never {
  throw new InvalidOperationException(
    "Collection was modified after the enumerator was instantiated.",
  );
}

/**
 * An index outside `0..count - 1` (`List` indexer, `fromEnd`, `removeAt`). The
 * `SortedList` members pass the index, which .NET appends (`Actual value was 5.`).
 */
export function throwOOR(actualValue?: number): never {
  throw new ArgumentOutOfRangeException(
    "index",
    actualValue,
    "Index was out of range. Must be non-negative and less than the size of the collection.",
  );
}

/** `List.insert` at an index outside `0..count`. */
export function throwInsertOOR(): never {
  throw new ArgumentOutOfRangeException(
    "index",
    undefined,
    "Index must be within the bounds of the List.",
  );
}

/** `insertRange` / `indexOf(item, index)` at an index outside `0..count`. */
export function throwIndexAtMostCount(): never {
  throw new ArgumentOutOfRangeException(
    "index",
    undefined,
    "Index was out of range. Must be non-negative and less than or equal to the size of the collection.",
  );
}

/** A negative index, count or capacity. */
export function throwNegative(paramName: string): never {
  throw new ArgumentOutOfRangeException(paramName, undefined, "Non-negative number required.");
}

/** An `index` / `count` pair that reaches past the end of the collection. */
export function throwInvalidRange(): never {
  throw new ArgumentException(
    "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.",
  );
}

/** A `null` argument .NET rejects. */
export function throwArgumentNull(paramName: string): never {
  throw new ArgumentNullException(paramName);
}

/** A dictionary read of a key it does not hold. */
export function throwKeyNotFound(key: unknown): never {
  throw new KeyNotFoundException(
    `The given key '${messageValue(key)}' was not present in the dictionary.`,
  );
}

/**
 * `Dictionary.add` (and `SortedList`'s copy constructor) of a key it already
 * holds; `SortedList.add` names the `key` parameter, and `SortedDictionary`
 * passes the `[key, value]` pair text.
 */
export function throwDuplicateKey(key: unknown, paramName?: string): never {
  throw new ArgumentException(
    `An item with the same key has already been added. Key: ${messageValue(key)}`,
    paramName,
  );
}

/** A size-changing member of a fixed-size adopted list (an array as `IList<T>`). */
export function throwFixedSize(): never {
  throw new NotSupportedException("Collection was of a fixed size.");
}

/** A mutator of a read-only collection (an array's `ICollection<T>.Clear`). */
export function throwReadOnly(): never {
  throw new NotSupportedException("Collection is read-only.");
}

export function throwEmptyQueue(): never {
  throw new InvalidOperationException("Queue empty.");
}

export function throwEmptyStack(): never {
  throw new InvalidOperationException("Stack empty.");
}

/**
 * A comparison that threw during `sort` / `binarySearch`, wrapped as .NET's
 * `Array.Sort` wraps it; the thrown value is the inner exception when it is an
 * `Error`.
 */
export function throwCompareFailed(inner: unknown): never {
  throw new InvalidOperationException(
    "Failed to compare two elements in the array.",
    inner instanceof Error ? inner : null,
  );
}

/** `Comparer<T>.Default` on two values that are not comparable. */
export function throwNotComparable(): never {
  throw new ArgumentException("At least one object must implement IComparable.");
}

/** A `copyTo` destination too small for the elements. */
export function throwArrayTooSmall(): never {
  throw new ArgumentException(
    "Destination array was not long enough. Check the destination index, length, and the array's lower bounds.",
    "destinationArray",
  );
}

/** A negative `copyTo` destination index, with .NET's text and the actual value. */
export function throwNegativeDestinationIndex(index: number): never {
  throw new ArgumentOutOfRangeException(
    "destinationIndex",
    index,
    `destinationIndex ('${messageValue(index)}') must be greater than or equal to '0'.`,
  );
}

/** A negative argument checked as .NET's `ThrowIfNegative` does (`<name> ('-1') must be a non-negative value.`). */
export function throwNegativeValue(paramName: string, value: number): never {
  throw new ArgumentOutOfRangeException(
    paramName,
    value,
    `${paramName} ('${messageValue(value)}') must be a non-negative value.`,
  );
}

/** A sorted class's `copyTo` into an array too short from the given index (.NET's collection-level text). */
export function throwCollectionTooSmall(): never {
  throw new ArgumentException(
    "Destination array is not long enough to copy all the items in the collection. Check array index and length.",
  );
}

/** `SortedSet.getViewBetween(lower, upper)` with `lower` above `upper`. */
export function throwViewBoundsInverted(): never {
  throw new ArgumentException("Must be less than or equal to upperValue.", "lowerValue");
}

/**
 * `ArgumentOutOfRangeException` with .NET's default text (`Specified argument was
 * out of the range of valid values.`): a `SortedSet` view's `add` (`item`) or a
 * nested view's bound (`lowerValue` / `upperValue`).
 */
export function throwArgumentOutOfRange(paramName: string): never {
  throw new ArgumentOutOfRangeException(paramName);
}

/** A mutator of `SortedList.Keys` / `.Values` other than the key indexer write. */
export function throwSortedListNested(): never {
  throw new NotSupportedException(
    "This operation is not supported on SortedList nested types because they require modifying the original SortedList.",
  );
}

/** `SortedList.Keys[i] = …`. */
export function throwKeyListWrite(): never {
  throw new NotSupportedException(
    "Mutating a key collection derived from a dictionary is not allowed.",
  );
}

/** `key`, or `ArgumentNullException` (`key`) for `null` / `undefined`, as every .NET dictionary does. */
export function requireKey<K>(key: K): K {
  if (key === null || key === undefined) {
    throwArgumentNull("key");
  }

  return key;
}

/** `new ConcurrentDictionary(concurrencyLevel, …)` with a level below 1 other than -1. */
export function throwConcurrencyLevel(): never {
  throw new ArgumentOutOfRangeException(
    "concurrencyLevel",
    undefined,
    "The concurrencyLevel argument must be positive, or -1 to indicate a default level.",
  );
}

/** `ConcurrentDictionary`'s `IDictionary<K, V>.Add` of a present key. */
export function throwConcurrentDuplicateKey(): never {
  throw new ArgumentException("The key already existed in the dictionary.");
}

/** `new ConcurrentDictionary(collection)` whose source repeats a key. */
export function throwConcurrentSourceDuplicate(): never {
  throw new ArgumentException("The source argument contains duplicate keys.");
}

/** `ImmutableList.Replace` of a value it does not hold. */
export function throwOldValueNotFound(): never {
  throw new ArgumentException("Cannot find the old value", "oldValue");
}

/** `ImmutableDictionary.Add` of a present key holding a different value. */
export function throwImmutableDuplicateKey(key: unknown): never {
  throw new ArgumentException(
    `An element with the same key but a different value already exists. Key: '${messageValue(key)}'`,
  );
}
