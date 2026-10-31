import { isString } from "metano-runtime";

export class OverloadOverrideException extends Error {
  readonly #baseMessage: string;

  readonly origin: Error | null = null;

  constructor(message: string, inner: Error);
  constructor(message: string);
  constructor();
  constructor(...args: unknown[]) {
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
}
