import { isString } from "metano-runtime";

export class OverloadOverrideException extends Error {
  protected static readonly byOverloadOverrideExceptionDefault: unique symbol = Symbol("OverloadOverrideException()");

  protected static readonly byOverloadOverrideExceptionMessage: unique symbol = Symbol("OverloadOverrideException(string message)");

  protected static readonly byOverloadOverrideExceptionMessageInner: unique symbol = Symbol("OverloadOverrideException(string message, Exception inner)");

  readonly #baseMessage: string;

  readonly origin: Error | null = null;

  constructor(overload: typeof OverloadOverrideException.byOverloadOverrideExceptionDefault);
  constructor(overload: typeof OverloadOverrideException.byOverloadOverrideExceptionMessage, message: string);
  constructor(overload: typeof OverloadOverrideException.byOverloadOverrideExceptionMessageInner, message: string, inner: Error);
  constructor(message: string, inner: Error);
  constructor(message: string);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === OverloadOverrideException.byOverloadOverrideExceptionDefault) {
      super("Exception of type 'App.OverloadOverrideException' was thrown.");
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === OverloadOverrideException.byOverloadOverrideExceptionMessage) {
      const message = args[1] as string;
      super(message);
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === OverloadOverrideException.byOverloadOverrideExceptionMessageInner) {
      const message = args[1] as string;
      const inner = args[2] as Error;
      const __chain0Arg0 = message;

      {
        const message = __chain0Arg0;
        super(message);
        this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
        Reflect.deleteProperty(this, "message");
      }

      this.origin = inner;

      return;
    }

    if (args.length === 2 && isString(args[0]) && typeof args[1] === "object") {
      const message = args[0] as string;
      const inner = args[1] as Error;
      const __chain0Arg0 = message;

      {
        const message = __chain0Arg0;
        super(message);
        this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
        Reflect.deleteProperty(this, "message");
      }

      this.origin = inner;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const message = args[0] as string;
      super(message);
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    if (args.length === 0) {
      super("Exception of type 'App.OverloadOverrideException' was thrown.");
      this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
      Reflect.deleteProperty(this, "message");

      return;
    }

    throw new Error("No matching constructor");
  }

  override get message(): string {
    return "wrapped: " + this.#baseMessage;
  }

  static fromDefault(): OverloadOverrideException {
    return new OverloadOverrideException(OverloadOverrideException.byOverloadOverrideExceptionDefault);
  }

  static fromMessage(message: string): OverloadOverrideException {
    return new OverloadOverrideException(OverloadOverrideException.byOverloadOverrideExceptionMessage, message);
  }

  static fromMessageInner(message: string, inner: Error): OverloadOverrideException {
    return new OverloadOverrideException(OverloadOverrideException.byOverloadOverrideExceptionMessageInner, message, inner);
  }
}
