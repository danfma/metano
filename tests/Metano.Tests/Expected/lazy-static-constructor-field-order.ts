import { typeInitializer } from "metano-runtime";
import { Counter } from "./counter";

export class FieldOrder {
  static readonly #$init: () => void = typeInitializer("FieldOrder", () => {
    Counter.reset();
  });

  name: string;
  a: number = (FieldOrder.#$init(), Counter.take());
  b: number = Counter.take();

  constructor() { }
}
