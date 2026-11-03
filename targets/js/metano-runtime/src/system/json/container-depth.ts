import { JsonException } from "./errors.ts";

/** System.Text.Json's default `JsonSerializerOptions.MaxDepth`. */
export const MAX_JSON_DEPTH = 64;

/** The STJ message for a write deeper than the maximum depth (FR-016). */
const WRITE_DEPTH_MESSAGE =
  "A possible object cycle was detected. This can either be due to a cycle or if the object " +
  `depth is larger than the maximum allowed depth of ${MAX_JSON_DEPTH}. Consider using ` +
  "ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.";

/** The JSON container a read enters, named as STJ's reader names it. */
export type JsonContainer = "object" | "array";

/**
 * The number of JSON containers (objects, arrays, dictionaries) currently
 * open in the running serialize or deserialize call. Serialization is
 * synchronous, so one counter serves every nested converter, including a
 * custom converter that calls back into the serializer.
 */
const openContainers = { count: 0 };

/**
 * Runs `write` inside one more JSON container. The 65th nested container
 * throws `JsonException` instead of overflowing the stack on a cycle, as
 * System.Text.Json does (measured on .NET 10: 64 nested objects are
 * written, the 65th throws).
 */
export function writeContainer<T>(write: () => T): T {
  return withinContainer(write, () => new JsonException(WRITE_DEPTH_MESSAGE));
}

/**
 * Runs `read` inside one more JSON container of the parsed value. The 65th
 * nested container throws STJ's reader message (measured on .NET 10: 64
 * nested containers are read, the 65th throws).
 */
export function readContainer<T>(container: JsonContainer, read: () => T): T {
  return withinContainer(
    read,
    () =>
      new JsonException(
        `The maximum configured depth of ${MAX_JSON_DEPTH} has been exceeded. ` +
          `Cannot read next JSON ${container}.`,
      ),
  );
}

function withinContainer<T>(run: () => T, depthExceeded: () => JsonException): T {
  if (openContainers.count >= MAX_JSON_DEPTH) {
    throw depthExceeded();
  }

  openContainers.count++;

  try {
    return run();
  } finally {
    openContainers.count--;
  }
}
