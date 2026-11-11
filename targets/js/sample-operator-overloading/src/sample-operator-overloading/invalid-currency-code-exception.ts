/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";

export class InvalidCurrencyCodeException extends ArgumentException {
  constructor(code: string) {
    super(`Unknown currency code '${code}'`, "code");
  }
}
