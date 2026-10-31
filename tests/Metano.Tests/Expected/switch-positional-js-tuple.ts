import type { Pair } from "./pair";

export class Pairs {
  constructor() { }

  static of(pair: Pair): string {
    return (($s) => {
      if ($s != null && $s[0] === 0) {
        const label = $s[1];

        return label;
      }

      return "";
    })(pair);
  }
}
