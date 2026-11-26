import { item$get, item$set, type Sig } from "sig-lib";

export class Use {
  private constructor() { }

  static bump(s: Sig): number {
    let receiver$temp$0;

    return (receiver$temp$0 = item$get(s, 1), item$set(s, 1, (receiver$temp$0 + 1) | 0), receiver$temp$0);
  }

  static write(s: Sig): void {
    item$set(s, 2, 7);
  }
}
