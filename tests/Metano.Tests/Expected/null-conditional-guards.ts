import { List } from "metano-runtime/system/collections";
import { score } from "./order-extensions.js";
import type { Order } from "./order.js";

export function normalize(value: string | null): string {
  return value?.trim() ?? "";
}

export function clear(order: Order | null): void {
  order?.items.clear();
}

export function remove(order: Order | null, item: number): boolean | null {
  return order?.items.remove(item) ?? null;
}

export function clearNext(order: Order | null): void {
  order?.getNext()?.items.clear();
}

export function third(order: Order | null): number | null {
  return order?.self.self.items.count ?? null;
}

export function scoreOf(order: Order | null): number | null {
  return order == null ? null : score(order);
}

export function first(items: List<number> | null): number | null {
  return items?.get(0) ?? null;
}

export function clearNextItems(order: Order | null): void {
  order?.next?.items.clear();
}

export function removeFromNext(order: Order | null, item: number): boolean | null {
  return order?.next?.items.remove(item) ?? null;
}

export async function removeLater(order: Order, item: Promise<number>): Promise<boolean | null> {
  return order.getNext()?.items.remove(await item) ?? null;
}
