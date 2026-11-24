/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { InvalidCurrencyCodeException } from "./invalid-currency-code-exception.js";

let UnsupportedCurrencyException$rt: ClassType<UnsupportedCurrencyException> | undefined;

export class UnsupportedCurrencyException extends InvalidCurrencyCodeException {
  override readonly name: string = "UnsupportedCurrencyException";
  readonly #baseMessage: string;

  constructor(code: string) {
    super(code);
    this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
    Reflect.deleteProperty(this, "message");
  }

  static override get $runtimeType(): ClassType<UnsupportedCurrencyException> {
    return (UnsupportedCurrencyException$rt ??= {
      kind: "class",
      name: "UnsupportedCurrencyException",
      fullName: "SampleOperatorOverloading.UnsupportedCurrencyException",
      namespace: "SampleOperatorOverloading",
      ctor: UnsupportedCurrencyException,
      base: () => InvalidCurrencyCodeException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is UnsupportedCurrencyException =>
        v instanceof UnsupportedCurrencyException,
    });
  }

  protected override get $typeName(): string {
    return "UnsupportedCurrencyException";
  }

  protected override get $typeNamespace(): string {
    return "SampleOperatorOverloading";
  }

  override get message(): string {
    return `Unsupported currency. ${this.#baseMessage}`;
  }
}
