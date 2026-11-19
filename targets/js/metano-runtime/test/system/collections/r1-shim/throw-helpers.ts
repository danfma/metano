// TEMPORARY test-only stand-in for R1's `throw-helpers.ts` (see preload.ts): the R0-frozen names and messages.
import { ArgumentException } from "../../../../src/system/exceptions/argument-exception.ts";
import { ArgumentNullException } from "../../../../src/system/exceptions/argument-null-exception.ts";
import { InvalidOperationException } from "../../../../src/system/exceptions/invalid-operation-exception.ts";
import { KeyNotFoundException } from "../../../../src/system/exceptions/key-not-found-exception.ts";
import { messageValue } from "../../../../src/system/exceptions/message-value.ts";

export function throwStackModified(): never {
  throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
}

export function throwArgumentNull(paramName: string): never {
  throw new ArgumentNullException(paramName);
}

export function throwKeyNotFound(key: unknown): never {
  throw new KeyNotFoundException(`The given key '${messageValue(key)}' was not present in the dictionary.`);
}

export function throwArrayTooSmall(): never {
  throw new ArgumentException(
    "Destination array was not long enough. Check the destination index, length, and the array's lower bounds.",
    "destinationArray",
  );
}
