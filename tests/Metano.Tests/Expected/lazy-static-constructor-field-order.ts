import { typeInitializer } from "metano-runtime";
import { Counter } from "./counter.js";

export class FieldOrder {
  static readonly #$init = /* @__PURE__ */ typeInitializer("FieldOrder", () => {
    Counter.reset();
  }) as () => void;

  declare name: string;
  declare a: number;
  declare b: number;

  constructor() {
    FieldOrder.#$init();
    this.name = undefined!;
    this.a = Counter.take();
    this.b = Counter.take();
  }

  static $initialize(): void {
    FieldOrder.#$init();
  }
}
