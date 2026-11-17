import { toException } from "./exceptions/to-exception.ts";
import { TypeInitializationException } from "./exceptions/type-initialization-exception.ts";

/**
 * The guard generated code calls before every use of a type that declares a static
 * constructor (spec 001 FR-066): the first call runs `initialize` (the static field
 * initializers, then the static constructor body), later calls return at once.
 *
 * As on .NET:
 * - the type counts as initialized as soon as `initialize` starts, so a re-entrant
 *   call (the initializer creating an instance of its own type, or a cycle through
 *   another type's initializer) returns without running it again;
 * - a value that escapes `initialize` is wrapped in a {@link TypeInitializationException}
 *   (a non-`Error` value goes through {@link toException} first), and every later
 *   call rethrows that same exception.
 */
export function typeInitializer(typeName: string, initialize: () => void): () => void {
  let state: "pending" | "started" | "failed" = "pending";
  let failure: TypeInitializationException | null = null;

  return () => {
    if (state === "started") {
      return;
    }

    if (state === "failed") {
      throw failure;
    }

    state = "started";

    try {
      initialize();
    } catch (error) {
      state = "failed";
      failure = new TypeInitializationException(typeName, toException(error));

      throw failure;
    }
  };
}
