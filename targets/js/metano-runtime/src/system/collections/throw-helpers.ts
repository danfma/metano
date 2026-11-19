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

/** An index outside `0..count - 1` (`List` indexer, `fromEnd`, `removeAt`). */
export function throwOOR(): never {
  throw new ArgumentOutOfRangeException(
    "index",
    undefined,
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

/** `Dictionary.add` of a key it already holds. */
export function throwDuplicateKey(key: unknown): never {
  throw new ArgumentException(
    `An item with the same key has already been added. Key: ${messageValue(key)}`,
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
