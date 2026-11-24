import type { Holder } from "./holder.js";

export class Access {
  constructor() { }

  all(h: Holder, maybe: Holder | null): void {
    const read = (h as any).count;
    const extra = (h as any).Extra;
    const called = (h as any).Run(1, "x");
    const item = (h as any)[0];
    const optional = (maybe as any)?.count ?? null;
    (h as any).count = 2;
    (h as any).count += 3;
    (h as any).count++;
    (h as any)[1] = 4;
    const viaStatic = (h as any).count;
  }
}
