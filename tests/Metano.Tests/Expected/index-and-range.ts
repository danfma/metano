import { List, fromEnd, type IList, type IReadOnlyList } from "metano-runtime/system/collections";

export class Sequences {
  declare private readonly _items: List<number>;

  constructor() {
    this._items = List.of<number>([7, 8, 9]);
  }

  lastOfArray(values: number[]): number {
    return fromEnd(values, 1);
  }

  fromEndOfList(values: List<number>, offset: number): number {
    return values.fromEnd(offset);
  }

  fromEndOfReadOnlyList(values: IReadOnlyList<number>): number {
    return values.get(values.count - (1 + 1));
  }

  fromEndOfIList(values: IList<number>): number {
    return values.get(values.count - 1);
  }

  lastChar(text: string): string {
    return text[text.length - 1]!;
  }

  lastItem(): number {
    return this._items.fromEnd(1);
  }

  middle(values: number[]): number[] {
    return values.slice(1, values.length - 1);
  }

  withoutLast(text: string): string {
    return text.slice(0, text.length - 1);
  }

  tail(values: List<number>): List<number> {
    return values.slice(1);
  }

  copy(values: number[]): number[] {
    return values.slice();
  }

  between(values: number[], start: number, end: number): number[] {
    return values.slice(start, end);
  }
}
