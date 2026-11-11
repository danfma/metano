/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isString } from "metano-runtime";
import { Exception } from "metano-runtime/system/exceptions/exception";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export class CurrencyConversionException extends Exception {
  protected static readonly byCurrencyConversionExceptionFromCodeToCode: unique symbol = Symbol(
    "CurrencyConversionException(string fromCode, string toCode)",
  );

  protected static readonly byCurrencyConversionExceptionFromCodeToCodeReason: unique symbol =
    Symbol("CurrencyConversionException(string fromCode, string toCode, Exception reason)");

  override readonly name: string = "CurrencyConversionException";

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
    reason: Exception,
  );
  constructor(fromCode: string, toCode: string, reason: Exception);
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
      const __chain0Arg2 = new InvalidOperationException("No exchange rate available");

      {
        const fromCode = __chain0Arg0;
        const toCode = __chain0Arg1;
        const reason = __chain0Arg2;
        super(`Cannot convert ${fromCode} to ${toCode}`, reason);
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
      const reason = args[3] as Exception;
      super(`Cannot convert ${fromCode} to ${toCode}`, reason);
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    if (args.length === 3 && isString(args[0]) && isString(args[1]) && args[2] instanceof Error) {
      const fromCode = args[0] as string;
      const toCode = args[1] as string;
      const reason = args[2] as Exception;
      super(`Cannot convert ${fromCode} to ${toCode}`, reason);
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    if (args.length === 2 && isString(args[0]) && isString(args[1])) {
      const fromCode = args[0] as string;
      const toCode = args[1] as string;
      const __chain0Arg0 = fromCode;
      const __chain0Arg1 = toCode;
      const __chain0Arg2 = new InvalidOperationException("No exchange rate available");

      {
        const fromCode = __chain0Arg0;
        const toCode = __chain0Arg1;
        const reason = __chain0Arg2;
        super(`Cannot convert ${fromCode} to ${toCode}`, reason);
        this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
        Reflect.deleteProperty(this, "message");
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  protected override get typeNamespace(): string {
    return "SampleOperatorOverloading";
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
    reason: Exception,
  ): CurrencyConversionException {
    return new CurrencyConversionException(
      CurrencyConversionException.byCurrencyConversionExceptionFromCodeToCodeReason,
      fromCode,
      toCode,
      reason,
    );
  }
}
