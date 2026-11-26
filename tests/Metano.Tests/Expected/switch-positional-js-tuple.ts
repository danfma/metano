import type { Pair } from "./pair.js";

export class Pairs {
  private constructor() { }

  static of(pair: Pair): string {
    {
      let $s = pair;

      if ($s != null && $s[0] === 0) {
        const label = $s[1];

        return label;
      }

      return "";
    }
  }
}
