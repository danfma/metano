import { typeInitializer } from "metano-runtime";

export class Registry {
  static readonly #$init = /* @__PURE__ */ typeInitializer("Registry", () => {
    Registry.#log$ = [] as Array<string>;
    Registry.#a$ = Registry.init("A");
    Registry.#b$ = Registry.init("B");
    Registry.log.push("cctor");
    Registry.instance = new Registry();
    const n = 1;
    Registry.log.push("n" + n);
  }) as () => void;

  static #log$: string[] | null = null;
  static #a$: number = 0;
  static #instance$: Registry | null = null;
  static #b$: number = 0;
  static #untouched$: number = 0;

  constructor() {
    Registry.#$init();
  }

  static get log(): string[] {
    Registry.#$init();

    return Registry.#log$ as string[];
  }

  static get a(): number {
    Registry.#$init();

    return Registry.#a$;
  }

  static set a(value: number) {
    Registry.#$init();
    Registry.#a$ = value;
  }

  static get instance(): Registry {
    Registry.#$init();

    return Registry.#instance$ as Registry;
  }

  private static set instance(value: Registry) {
    Registry.#$init();
    Registry.#instance$ = value;
  }

  static get b(): number {
    Registry.#$init();

    return Registry.#b$;
  }

  static set b(value: number) {
    Registry.#$init();
    Registry.#b$ = value;
  }

  static get untouched(): number {
    Registry.#$init();

    return Registry.#untouched$;
  }

  static set untouched(value: number) {
    Registry.#$init();
    Registry.#untouched$ = value;
  }

  private static init(text: string): number {
    Registry.#$init();
    Registry.log.push(text);

    return 1;
  }
}
