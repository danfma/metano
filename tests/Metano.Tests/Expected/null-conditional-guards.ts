import { listRemove } from "metano-runtime";
import type { Order } from "./order";

export function normalize(value: string | null): string {
  return value?.trim() ?? "";
}

export function clear(order: Order | null): void {
  order == null ? null : order.items.length = 0;
}

export function remove(order: Order | null, item: number): boolean | null {
  return order == null ? null : listRemove(order.items, item);
}

export function clearNext(order: Order | null): void {
  ((receiver$temp$0) => receiver$temp$0 == null ? null : receiver$temp$0.items.length = 0)(order?.getNext());
}

export function third(order: Order | null): number | null {
  return order?.next.next.items.length;
}

export async function removeLater(order: Order, item: Promise<number>): Promise<boolean | null> {
  return await (async (receiver$temp$0) => receiver$temp$0 == null ? null : listRemove(receiver$temp$0.items, await item))(order.getNext());
}
