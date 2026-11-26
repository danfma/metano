import { isInt32, typeInitializer } from "metano-runtime";

export class Meter {
  static readonly #$init = /* @__PURE__ */ typeInitializer("Meter", () => {
    Meter.scale = 10;
  }) as () => void;

  static #scale$: number = 0;
  declare v: number;

  constructor(v: number) {
    Meter.#$init();
    this.v = v;
  }

  static get scale(): number {
    Meter.#$init();

    return Meter.#scale$;
  }

  private static set scale(value: number) {
    Meter.#$init();
    Meter.#scale$ = value;
  }

  static $initialize(): void {
    Meter.#$init();
  }

  static __addMeterMeter(a: Meter, b: Meter): Meter {
    Meter.#$init();

    return new Meter((a.v + b.v) | 0);
  }

  static __addMeterInt32(a: Meter, k: number): Meter {
    Meter.#$init();

    return new Meter((a.v + k) | 0);
  }

  static __add(a: Meter, b: Meter): Meter;
  static __add(a: Meter, k: number): Meter;
  static __add(...args: unknown[]): Meter {
    Meter.#$init();

    if (args.length === 2 && args[0] instanceof Meter && args[1] instanceof Meter) {
      return Meter.__addMeterMeter(args[0] as Meter, args[1] as Meter);
    }

    if (args.length === 2 && args[0] instanceof Meter && isInt32(args[1])) {
      return Meter.__addMeterInt32(args[0] as Meter, args[1] as number);
    }

    throw new Error("No matching overload for add");
  }

  $add(b: Meter): Meter;
  $add(k: number): Meter;
  $add(...args: unknown[]): Meter {
    if (args.length === 1 && args[0] instanceof Meter) {
      return Meter.__addMeterMeter(this, args[0] as Meter);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return Meter.__addMeterInt32(this, args[0] as number);
    }

    throw new Error("No matching overload for add");
  }

  static __negate(a: Meter): Meter {
    Meter.#$init();

    return new Meter(-a.v | 0);
  }

  $negate(): Meter {
    return Meter.__negate(this);
  }

  static __toInt32(m: Meter): number {
    Meter.#$init();

    return Math.imul(m.v, Meter.scale);
  }

  static __implicitFromInt32(v: number): Meter {
    Meter.#$init();

    return new Meter(v);
  }
}
