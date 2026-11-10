export async function awaitedValue(n: number | null): Promise<number | null> {
  try {
    const read = await Promise.resolve(n);

    return (read ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()) + 1;
  } catch (_err) {
    return null;
  }
}

export async function awaitedCast(nl: bigint | null): Promise<number | null> {
  try {
    const read = await Promise.resolve(nl);

    return Number(read ?? (() => {
      throw new Error("Nullable object must have a value.");
    })());
  } catch (_err) {
    return null;
  }
}

export function suppressedValue(n: number | null): number | null {
  try {
    return (n ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()) + 1;
  } catch (_err) {
    return null;
  }
}

export function disabledContextValue(n: number | null): number | null {
  try {
    return (n ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()) + 1;
  } catch (_err) {
    return null;
  }
}
