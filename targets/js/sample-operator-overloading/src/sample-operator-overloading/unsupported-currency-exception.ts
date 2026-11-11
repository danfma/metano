/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { InvalidCurrencyCodeException } from "./invalid-currency-code-exception";

export class UnsupportedCurrencyException extends InvalidCurrencyCodeException {
  override readonly name: string = "UnsupportedCurrencyException";

  readonly #baseMessage: string;

  constructor(code: string) {
    super(code);
    this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
    Reflect.deleteProperty(this, "message");
  }

  protected override get typeNamespace(): string {
    return "SampleOperatorOverloading";
  }

  override get message(): string {
    return `Unsupported currency. ${this.#baseMessage}`;
  }
}
