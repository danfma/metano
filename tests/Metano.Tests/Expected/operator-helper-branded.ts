export type Qty = number & { readonly __brand: "Qty" };

export namespace Qty {
  export function create(value: number): Qty {
    return value as Qty;
  }

  export function toString(value: Qty): string {
    return String(value);
  }

  export function __add(a: Qty, b: Qty): Qty {
    return Qty.create((a + b) | 0);
  }

  export function __implicitFromInt32(v: number): Qty {
    return Qty.create(Math.imul(v, 2));
  }

  export function __toInt32(q: Qty): number {
    return (q + 7) | 0;
  }
}
