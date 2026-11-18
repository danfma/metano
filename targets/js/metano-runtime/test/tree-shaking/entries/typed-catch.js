// A typed catch, as generated code lowers it (spec 011 FR-015).
import { toException, tryFilter } from "./index.js";
import { InvalidOperationException } from "./system/exceptions/invalid-operation-exception.js";

export function attempt(work) {
  try {
    return work();
  } catch (e$) {
    const e0 = toException(e$);

    if (e0 instanceof InvalidOperationException && tryFilter(() => e0.message.length > 0)) {
      return e0.message;
    }

    throw e$;
  }
}
