/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isString } from "metano-runtime";

export class CurrencyConversionException extends Error {
  protected static readonly byCurrencyConversionExceptionFromCodeToCode: unique symbol = Symbol(
    "CurrencyConversionException(string fromCode, string toCode)",
  );

  protected static readonly byCurrencyConversionExceptionFromCodeToCodeReason: unique symbol =
    Symbol("CurrencyConversionException(string fromCode, string toCode, Exception reason)");

  readonly #baseMessage: string;

  constructor(
    overload: typeof CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCode,
    fromCode: string,
    toCode: string,
  );
  constructor(
    overload: typeof CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCodeReason,
    fromCode: string,
    toCode: string,
    reason: Error,
  );
  constructor(fromCode: string, toCode: string, reason: Error);
  constructor(fromCode: string, toCode: string);
  constructor(...args: unknown[]) {
    if (
      typeof args[0] === "symbol" &&
      args[0] === CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCode
    ) {
      const fromCode = args[1] as string;
      const toCode = args[2] as string;
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

    if (
      typeof args[0] === "symbol" &&
      args[0] === CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCodeReason
    ) {
      const fromCode = args[1] as string;
      const toCode = args[2] as string;
      const reason = args[3] as Error;
      super(`Cannot convert ${fromCode} to ${toCode}`, { cause: reason });
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

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

  static fromFromCodeToCode(fromCode: string, toCode: string): CurrencyConversionException {
    return new CurrencyConversionException(
      CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCode,
      fromCode,
      toCode,
    );
  }

  static fromFromCodeToCodeReason(
    fromCode: string,
    toCode: string,
    reason: Error,
  ): CurrencyConversionException {
    return new CurrencyConversionException(
      CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCodeReason,
      fromCode,
      toCode,
      reason,
    );
  }
}
