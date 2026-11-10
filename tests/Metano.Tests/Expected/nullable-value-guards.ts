import type { Holder } from "./holder";

export function checkedLocal(n: number | null): number {
  if (n != null) {
    return n ?? (() => {
      throw new Error("Nullable object must have a value.");
    })();
  }

  return -1;
}

export function suppressed(n: number | null): number {
  return n ?? (() => {
    throw new Error("Nullable object must have a value.");
  })();
}

export function lambdaCleared(n: number | null): number {
  n = 1;
  const clear = () => n = null;
  clear();

  return n ?? (() => {
    throw new Error("Nullable object must have a value.");
  })();
}

export function clearedByACall(h: Holder): number {
  if (h.p != null) {
    h.clear();

    return h.p ?? (() => {
      throw new Error("Nullable object must have a value.");
    })();
  }

  return -1;
}

export async function awaited(pending: Promise<number | null>): Promise<number> {
  const n = await pending;

  return n ?? (() => {
    throw new Error("Nullable object must have a value.");
  })();
}

export function toDouble(nl: bigint | null): number {
  return Number(nl ?? (() => {
    throw new Error("Nullable object must have a value.");
  })());
}

export function suppressedCast(n: number | null): number {
  return n ?? (() => {
    throw new Error("Nullable object must have a value.");
  })();
}

export function flag(b: boolean | null): string {
  return b ?? (() => {
    throw new Error("Nullable object must have a value.");
  })() ? "t" : "f";
}

export function widened(t: [number, number] | null): [bigint, bigint] {
  return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(t ?? (() => {
    throw new Error("Nullable object must have a value.");
  })());
}

export function wrappedCast(x: number): number {
  return x;
}

export function wrappedConstant(): number {
  return 5;
}

export function rewrappedNullable(n: number | null): number {
  return n ?? (() => {
    throw new Error("Nullable object must have a value.");
  })();
}

export function disabled(n: number | null): number {
  return n ?? (() => {
    throw new Error("Nullable object must have a value.");
  })();
}
