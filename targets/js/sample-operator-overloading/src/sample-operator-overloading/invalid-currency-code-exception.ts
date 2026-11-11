/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";

export class InvalidCurrencyCodeException extends ArgumentException {
  override readonly name: string = "InvalidCurrencyCodeException";

  constructor(code: string) {
    super(`Unknown currency code '${code}'`, "code");
  }

  protected override get $typeName(): string {
    return "InvalidCurrencyCodeException";
  }

  protected override get $typeNamespace(): string {
    return "SampleOperatorOverloading";
  }
}
