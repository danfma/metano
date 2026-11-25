import { throwError, toException } from "metano-runtime";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export async function awaitedValue(n: number | null): Promise<number | null> {
  try {
    const read = await Promise.resolve(n);

    return (read ?? throwError(new InvalidOperationException("Nullable object must have a value."))) + 1;
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return null;
    } else {
      throw e$;
    }
  }
}

export async function awaitedCast(nl: bigint | null): Promise<number | null> {
  try {
    const read = await Promise.resolve(nl);

    return Number(read ?? throwError(new InvalidOperationException("Nullable object must have a value.")));
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return null;
    } else {
      throw e$;
    }
  }
}

export function suppressedValue(n: number | null): number | null {
  try {
    return (n ?? throwError(new InvalidOperationException("Nullable object must have a value."))) + 1;
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return null;
    } else {
      throw e$;
    }
  }
}

export function disabledContextValue(n: number | null): number | null {
  try {
    return (n ?? throwError(new InvalidOperationException("Nullable object must have a value."))) + 1;
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return null;
    } else {
      throw e$;
    }
  }
}
