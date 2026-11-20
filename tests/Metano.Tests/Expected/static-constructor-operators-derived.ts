import { typeInitializer } from "metano-runtime";
import { Meter } from "./meter";

export class SubMeter extends Meter {
  static readonly #$init = /* @__PURE__ */ typeInitializer("SubMeter", () => {
    SubMeter.offset = 1;
  }) as () => void;

  static #offset$: number = 0;

  constructor(v: number) {
    SubMeter.#$init();
    super(v);
  }

  static get offset(): number {
    SubMeter.#$init();

    return SubMeter.#offset$;
  }

  private static set offset(value: number) {
    SubMeter.#$init();
    SubMeter.#offset$ = value;
  }

  static $initialize(): void {
    SubMeter.#$init();
  }

  static override __toInt32(m: SubMeter): number {
    SubMeter.#$init();

    return (m.v + SubMeter.offset) | 0;
  }
}
