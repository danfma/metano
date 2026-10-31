/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isString } from "metano-runtime";

export class CurrencyConversionException extends Error {
  readonly #baseMessage: string;

  constructor(fromCode: string, toCode: string, reason: Error);
  constructor(fromCode: string, toCode: string);
  constructor(...args: unknown[]) {
    if (
      args.length === 3 &&
      isString(args[0]) &&
      isString(args[1]) &&
      typeof args[2] === "object"
    ) {
      const fromCode = args[0] as string;
      const toCode = args[1] as string;
      const reason = args[2] as Error;
      super(`Cannot convert ${fromCode} to ${toCode}`, { cause: reason });
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    if (args.length === 2 && isString(args[0]) && isString(args[1])) {
      const fromCode = args[0] as string;
      const toCode = args[1] as string;
      const __chain0Arg0 = fromCode;
      const __chain0Arg1 = toCode;
      const __chain0Arg2 = new Error("No exchange rate available");

      {
        const fromCode = __chain0Arg0;
        const toCode = __chain0Arg1;
        const reason = __chain0Arg2;
        super(`Cannot convert ${fromCode} to ${toCode}`, { cause: reason });
        this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
        Reflect.deleteProperty(this, "message");
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  override get message(): string {
    return `Conversion failed: ${this.#baseMessage}`;
  }
}
