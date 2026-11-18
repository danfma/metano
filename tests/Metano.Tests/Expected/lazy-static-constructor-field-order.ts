import { typeInitializer } from "metano-runtime";
import { Counter } from "./counter";

export class FieldOrder {
  static readonly #$init = /* @__PURE__ */ typeInitializer("FieldOrder", () => {
    Counter.reset();
  }) as () => void;

  name: string;
  a: number = (FieldOrder.#$init(), Counter.take());
  b: number = Counter.take();

  constructor() { }
}
