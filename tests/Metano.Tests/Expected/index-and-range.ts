export class Sequences {
  private readonly _items: number[] = [7, 8, 9];

  constructor() { }

  lastOfArray(values: number[]): number {
    return values[values.length - 1];
  }

  fromEndOfList(values: number[], offset: number): number {
    return values[values.length - offset];
  }

  fromEndOfReadOnlyList(values: number[]): number {
    return values[values.length - (1 + 1)];
  }

  fromEndOfIList(values: number[]): number {
    return values[values.length - 1];
  }

  lastChar(text: string): string {
    return text[text.length - 1];
  }

  lastItem(): number {
    return this._items[this._items.length - 1];
  }

  middle(values: number[]): number[] {
    return values.slice(1, values.length - 1);
  }

  withoutLast(text: string): string {
    return text.slice(0, text.length - 1);
  }

  tail(values: number[]): number[] {
    return values.slice(1);
  }

  copy(values: number[]): number[] {
    return values.slice();
  }

  between(values: number[], start: number, end: number): number[] {
    return values.slice(start, end);
  }
}
