export type Qty = number & { readonly __brand: "Qty" };

function Qty$create(value: number): Qty {
  return value as Qty;
}

function Qty$toString(value: Qty): string {
  return String(value);
}

function Qty$__add(a: Qty, b: Qty): Qty {
  return Qty.create((a + b) | 0);
}

function Qty$__implicitFromInt32(v: number): Qty {
  return Qty.create(Math.imul(v, 2));
}

function Qty$__toInt32(q: Qty): number {
  return (q + 7) | 0;
}

export const Qty = {
  create: Qty$create,
  toString: Qty$toString,
  __add: Qty$__add,
  __implicitFromInt32: Qty$__implicitFromInt32,
  __toInt32: Qty$__toInt32,
} as const;
