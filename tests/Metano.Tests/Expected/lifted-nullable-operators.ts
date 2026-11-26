import { List } from "metano-runtime/system/collections";
import type { Order } from "./order.js";

export function concatCount(o: Order | null): string {
  return "n" + (o?.items.count ?? "");
}

export function coalesceSum(o: Order | null): number {
  return (o == null ? null : (o.items.count + 1) | 0) ?? 0;
}

export function addQty(o: Order, k: number): number | null {
  return o.qty == null ? null : (o.qty + k) | 0;
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
  const receiver$temp$0 = o.nextQty();
  const receiver$temp$1 = o.nextQty();

  return receiver$temp$0 == null || receiver$temp$1 == null ? null : (receiver$temp$0 + receiver$temp$1) | 0;
}

export function nestedPlus(o: Order | null): number | null {
  let $receiver;

  return ($receiver = o?.next, $receiver == null ? null : $receiver.qty == null ? null : ($receiver.qty + 1) | 0);
}

export function show(o: Order): string {
  return `q=${o.qty ?? ""}`;
}

export function orders(): List<Order | null> {
  return List.of<Order | null>([null]);
}
