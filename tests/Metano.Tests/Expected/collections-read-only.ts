import { List, ReadOnlyList, asReadOnly, type IList, type IReadOnlyList } from "metano-runtime/system/collections";
import { arrayLength, formatBoolean } from "metano-runtime";

export class Views {
  private constructor() { }

  static wrap(list: List<number>): ReadOnlyList<number> {
    return asReadOnly(list);
  }

  static ofArray(items: string[]): ReadOnlyList<string> {
    return ReadOnlyList.fromArray(items);
  }

  static construct(source: IList<number>): ReadOnlyList<number> {
    return new ReadOnlyList<number>(source);
  }

  static none(): ReadOnlyList<number> {
    return ReadOnlyList.empty();
  }

  static describe(view: ReadOnlyList<number>): string {
    const first = view.count > 0 ? view.get(0) : -1;
    const last = view.count > 0 ? view.fromEnd(1) : -1;
    const copy = new Array<number>(arrayLength(view.count)).fill(0);
    view.copyTo(copy, 0);

    return `${view.count}:${first}:${last}:${formatBoolean(view.contains(2))}:${view.indexOf(3)}:${copy.length}`;
  }

  static sum(view: ReadOnlyList<number>): number {
    let sum = 0;
    for (const item of view) {
      sum = (sum + item) | 0;
    }

    return sum;
  }

  static asInterface(view: ReadOnlyList<number>): IReadOnlyList<number> {
    return view;
  }
}
