import { listRemove } from "metano-runtime";
import type { Order } from "./order";
import { score } from "./order-extensions";

export function normalize(value: string | null): string {
  return value?.trim() ?? "";
}

export function clear(order: Order | null): void {
  if (order != null) {
    order.items.length = 0;
  }
}

export function remove(order: Order | null, item: number): boolean | null {
  return order == null ? null : listRemove(order.items, item);
}

export function clearNext(order: Order | null): void {
  {
    const receiver$temp$0 = order?.getNext();

    if (receiver$temp$0 != null) {
      receiver$temp$0.items.length = 0;
    }
  }
}

export function third(order: Order | null): number | null {
  return order?.self.self.items.length;
}

export function scoreOf(order: Order | null): number | null {
  return order == null ? null : score(order);
}

export function first(items: number[] | null): number | null {
  return items?.[0];
}

export async function removeLater(order: Order, item: Promise<number>): Promise<boolean | null> {
  return await (async (receiver$temp$0) => receiver$temp$0 == null ? null : listRemove(receiver$temp$0.items, await item))(order.getNext());
}
