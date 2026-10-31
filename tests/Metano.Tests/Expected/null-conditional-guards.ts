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
    const $receiver = order?.getNext();

    if ($receiver != null) {
      $receiver.items.length = 0;
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

export function clearNextItems(order: Order | null): void {
  {
    const $receiver = order?.next;

    if ($receiver != null) {
      $receiver.items.length = 0;
    }
  }
}

export function removeFromNext(order: Order | null, item: number): boolean | null {
  return (($receiver) => $receiver == null ? null : listRemove($receiver.items, item))(order?.next);
}

export async function removeLater(order: Order, item: Promise<number>): Promise<boolean | null> {
  return await (async ($receiver) => $receiver == null ? null : listRemove($receiver.items, await item))(order.getNext());
}
