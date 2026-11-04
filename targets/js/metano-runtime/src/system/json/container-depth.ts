import { JsonException } from "./errors.ts";

/** System.Text.Json's default `JsonSerializerOptions.MaxDepth`. */
const MAX_JSON_DEPTH = 64;

/** The STJ metadata-mode message for a write deeper than the maximum depth (FR-016). */
const WRITE_DEPTH_MESSAGE =
  "A possible object cycle was detected. This can either be due to a cycle or if the object " +
  `depth is larger than the maximum allowed depth of ${MAX_JSON_DEPTH}. Consider using ` +
  "ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.";

/** The JSON container a read enters, named as STJ's reader names it. */
export type JsonContainer = "object" | "array";

/**
 * The number of JSON containers (objects, arrays, dictionaries) currently
 * open. The counter is process-global: it is safe only because no
 * serialize or deserialize call yields (no `await`, no callback deferred
 * past the call), so one call's containers are always closed before another
 * call starts. A custom converter that calls back into the serializer for a
 * separate root inherits the outer depth, where STJ's
 * `JsonSerializer.Serialize(…)` inside a converter starts a fresh writer at
 * depth 0.
 */
const openContainers = { count: 0 };

/**
 * Runs `write` inside one more JSON container; the 65th nested container
 * throws `JsonException` instead of overflowing the stack on a cycle.
 *
 * This is a hybrid of System.Text.Json's two write modes (measured on .NET
 * 10, DECIDED-20 b): the boundary is the source-generated fast path's, where
 * only containers count (64 nested objects are written, the 65th throws
 * `InvalidOperationException`), while the exception and message are the
 * metadata / reflection mode's, which also throws when a non-null scalar is
 * written inside the 64th container.
 */
export function writeContainer<T>(write: () => T): T {
  enterWriteContainer();

  try {
    return write();
  } finally {
    exitContainer();
  }
}

/**
 * Runs `read` inside one more JSON container of the parsed value. The 65th
 * nested container throws STJ's reader message (measured on .NET 10: 64
 * nested containers are read, the 65th throws).
 */
export function readContainer<T>(container: JsonContainer, read: () => T): T {
  enterReadContainer(container);

  try {
    return read();
  } finally {
    exitContainer();
  }
}

/**
 * Opens one more container for a write, as `writeContainer` does. The
 * caller closes it with `exitContainer` in a `finally`, which keeps hot
 * paths free of a closure per container (#456).
 */
export function enterWriteContainer(): void {
  if (openContainers.count >= MAX_JSON_DEPTH) {
    throw new JsonException(WRITE_DEPTH_MESSAGE);
  }

  openContainers.count++;
}

/** Opens one more container for a read, as `readContainer` does; see `enterWriteContainer`. */
export function enterReadContainer(container: JsonContainer): void {
  if (openContainers.count >= MAX_JSON_DEPTH) {
    throw new JsonException(readDepthMessage(container));
  }

  openContainers.count++;
}

/** Closes the container the last `enterWriteContainer` / `enterReadContainer` opened. */
export function exitContainer(): void {
  openContainers.count--;
}

/** STJ's reader message for a container past the maximum depth. */
function readDepthMessage(container: JsonContainer): string {
  return (
    `The maximum configured depth of ${MAX_JSON_DEPTH} has been exceeded. ` +
    `Cannot read next JSON ${container}.`
  );
}
