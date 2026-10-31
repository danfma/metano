import type { Order } from "./order";

export function concatCount(o: Order | null): string {
  return "n" + (o?.items.length ?? "");
}

export function coalesceSum(o: Order | null): number {
  return (o == null ? null : o.items.length + 1) ?? 0;
}

export function addQty(o: Order, k: number): number | null {
  return o.qty == null ? null : o.qty + k;
}

export function lessQty(o: Order): boolean {
  return o.qty != null && o.qty < 5;
}

export function hasQty(o: Order): boolean {
  return o.qty != null;
}

export function qtyOrSeven(o: Order): number {
  return o.qty ?? 7;
}

export function twice(o: Order): number | null {
  return ((receiver$temp$0) => ((receiver$temp$1) => receiver$temp$0 == null || receiver$temp$1 == null ? null : receiver$temp$0 + receiver$temp$1)(o.nextQty()))(o.nextQty());
}

export function nestedPlus(o: Order | null): number | null {
  return (($receiver) => $receiver == null ? null : $receiver.qty == null ? null : $receiver.qty + 1)(o?.next);
}

export function show(o: Order): string {
  return `q=${o.qty ?? ""}`;
}

export function orders(): (Order | null)[] {
  return [null];
}
