import { tryGetValue, tryParseInt32 } from "metano-runtime";

export class Glossary {
  private readonly _words: Map<string, string> = new Map();

  private readonly _counts: Map<string, number> = new Map();

  private readonly _notes: Map<string, string | null> = new Map();

  private _last: number = 0;

  constructor() { }

  translate(key: string): string {
    const [$found0, text] = tryGetValue(this._words, key, null);

    return $found0 ? text : (() => {
      throw new Error(key);
    })();
  }

  countOrZero(key: string): number {
    const [, count] = tryGetValue(this._counts, key, 0);

    return count;
  }

  increment(key: string): number {
    const [$found0, count] = tryGetValue(this._counts, key, 0);

    if (!$found0) {
      return -1;
    }

    return count + 1;
  }

  describe(key: string): string {
    const [found, text] = tryGetValue(this._words, key, null);

    return found ? text : "?";
  }

  hasNote(key: string): boolean {
    const [$found0] = tryGetValue(this._notes, key, null);

    return $found0;
  }

  note(key: string): string | null {
    let note: string | null;
    const [$found0, $out1] = tryGetValue(this._notes, key, null);
    note = $out1;

    return $found0 ? note : null;
  }

  remember(key: string): void {
    const [, $out0] = tryGetValue(this._counts, key, 0);
    this._last = $out0;
  }

  counter(): (arg: string) => number {
    return (key: string) => {
      const [$found0, c] = tryGetValue(this._counts, key, 0);

      return $found0 ? c : 0;
    };
  }

  shout(check: boolean, key: string): string {
    if (check) {
      const [$found0, w] = tryGetValue(this._words, key, null);

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
