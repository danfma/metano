/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Exception } from "metano-runtime/system/exceptions/exception";
import type { Currency } from "./currency";

export class NoSameMoneyCurrencyException extends Exception {
  constructor(expected: Currency, provided: Currency) {
    super(`Not same currency. Money has ${provided}, but expected ${expected}`);
  }
}
