import { Holder } from "./holder";

export class Calls {
  constructor() { }

  static run(maybe: Holder | null): number {
    const holder = new Holder(1);
    const a = holder.get();
    const b = holder.addField(2);
    const c = Holder.staticGet();
    holder.log(5);
    const d = holder.twice(4);
    const e = maybe?.get() ?? -1;
    const f = holder.get();
    const g = holder.renamedGet();
    const h = maybe?.get() ?? -1;

    return a + b + c + d + e + f + g + h;
  }
}
