import { Dictionary, type IReadOnlyDictionary } from "metano-runtime/system/collections";
import { throwError, tryParseInt32 } from "metano-runtime";
import { KeyNotFoundException } from "metano-runtime/system/exceptions/key-not-found-exception";

export class Glossary {
  declare private readonly _words: Dictionary<string, string>;
  declare private readonly _counts: Dictionary<string, number>;
  declare private readonly _notes: IReadOnlyDictionary<string, string | null>;
  declare private _last: number;

  constructor() {
    this._words = (new Dictionary<string, string>());
    this._counts = (new Dictionary<string, number>());
    this._notes = (new Dictionary<string, string | null>());
    this._last = 0;
  }

  translate(key: string): string {
    const [$found0, text] = this._words.tryGetValue(key, null);

    return $found0 ? text : throwError(new KeyNotFoundException(key));
  }

  countOrZero(key: string): number {
    const [, count] = this._counts.tryGetValue(key, 0);

    return count;
  }

  increment(key: string): number {
    const [$found0, count] = this._counts.tryGetValue(key, 0);

    if (!$found0) {
      return -1;
    }

    return (count + 1) | 0;
  }

  describe(key: string): string {
    const [found, text] = this._words.tryGetValue(key, null);

    return found ? text : "?";
  }

  hasNote(key: string): boolean {
    const [$found0] = this._notes.tryGetValue(key, null);

    return $found0;
  }

  note(key: string): string | null {
    let note: string | null;
    const [$found0, $out1] = this._notes.tryGetValue(key, null);
    note = $out1;

    return $found0 ? note : null;
  }

  remember(key: string): void {
    const [, $out0] = this._counts.tryGetValue(key, 0);
    this._last = $out0;
  }

  counter(): (arg: string) => number {
    return (key: string) => {
      const [$found0, c] = this._counts.tryGetValue(key, 0);

      return $found0 ? c : 0;
    };
  }

  shout(check: boolean, key: string): string {
    if (check) {
      const [$found0, w] = this._words.tryGetValue(key, null);

      if ($found0 && w.length > 3) {
        return w.toUpperCase();
      }
    }

    return key;
  }

  parseOrZero(s: string): number {
    const [$found0, n] = tryParseInt32(s);

    return $found0 ? n : 0;
  }
}
