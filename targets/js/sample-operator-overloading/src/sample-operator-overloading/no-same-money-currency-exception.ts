/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Currency } from "./currency";

export class NoSameMoneyCurrencyException extends Error {
  constructor(expected: Currency, provided: Currency) {
    super(`Not same currency. Money has ${provided}, but expected ${expected}`);
  }
}
