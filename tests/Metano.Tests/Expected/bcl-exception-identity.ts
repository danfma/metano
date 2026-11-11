import { isString } from "metano-runtime";
import { Decimal } from "decimal.js";
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";
import { ArgumentNullException } from "metano-runtime/system/exceptions/argument-null-exception";
import { ArgumentOutOfRangeException } from "metano-runtime/system/exceptions/argument-out-of-range-exception";
import { ArithmeticException } from "metano-runtime/system/exceptions/arithmetic-exception";
import { Exception } from "metano-runtime/system/exceptions/exception";
import { FormatException } from "metano-runtime/system/exceptions/format-exception";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import { KeyNotFoundException } from "metano-runtime/system/exceptions/key-not-found-exception";
import { ObjectDisposedException } from "metano-runtime/system/exceptions/object-disposed-exception";

export class InvalidAmountException extends ArgumentOutOfRangeException {
  override readonly name: string = "InvalidAmountException";

  constructor(amount: Decimal) {
    super("amount", amount, "The amount must be positive.");
  }

  protected override get typeNamespace(): string {
    return "Billing";
  }
}

export class LedgerException extends Exception {
  protected static readonly byLedgerExceptionDefault: unique symbol = Symbol("LedgerException()");

  protected static readonly byLedgerExceptionMessageInner: unique symbol = Symbol("LedgerException(string message, Exception inner)");

  override readonly name: string = "LedgerException";

  constructor(overload: typeof LedgerException.byLedgerExceptionDefault);
  constructor(overload: typeof LedgerException.byLedgerExceptionMessageInner, message: string, inner: Exception);
  constructor(message: string, inner: Exception);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === LedgerException.byLedgerExceptionDefault) {
      super("Exception of type 'Billing.LedgerException' was thrown.");

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === LedgerException.byLedgerExceptionMessageInner) {
      const message = args[1] as string;
      const inner = args[2] as Exception;
      super(message, inner);

      return;
    }

    if (args.length === 2 && isString(args[0]) && args[1] instanceof Error) {
      const message = args[0] as string;
      const inner = args[1] as Exception;
      super(message, inner);

      return;
    }

    if (args.length === 0) {
      super("Exception of type 'Billing.LedgerException' was thrown.");

      return;
    }

    throw new Error("No matching constructor");
  }

  protected override get typeNamespace(): string {
    return "Billing";
  }

  static fromDefault(): LedgerException {
    return new LedgerException(LedgerException.byLedgerExceptionDefault);
  }

  static fromMessageInner(message: string, inner: Exception): LedgerException {
    return new LedgerException(LedgerException.byLedgerExceptionMessageInner, message, inner);
  }
}

export class StaleLedgerException extends InvalidOperationException {
  override readonly name: string = "StaleLedgerException";

  constructor() {
    super();
  }

  protected override get typeNamespace(): string {
    return "Billing";
  }
}

export class Ledger {
  constructor() { }

  static post(account: string | null, amount: Decimal, attempts: number): void {
    if (account == null) {
      throw new ArgumentNullException("account");
    }

    if (amount.lte(new Decimal("0"))) {
      throw new InvalidAmountException(amount);
    }

    if (attempts > 3) {
      throw new ArgumentOutOfRangeException("attempts", attempts, "Too many attempts.");
    }

    if (account.length === 0) {
      throw new ArgumentException("The account is empty.", "account");
    }

    throw new KeyNotFoundException(`No account '${account}'.`);
  }

  static wrap(inner: Exception): Exception {
    return new InvalidOperationException("Posting failed.", inner);
  }

  static closed(): Exception {
    return new ObjectDisposedException("Ledger");
  }

  static classify(error: Exception): string {
    return (($s) => {
      if ($s instanceof ArgumentNullException) {
        return "null";
      }

      if ($s instanceof ArgumentException) {
        const argument = $s;

        return "argument:" + argument.paramName;
      }

      if ($s instanceof StaleLedgerException) {
        return "stale";
      }

      if ($s instanceof InvalidOperationException) {
        return "state";
      }

      return "other";
    })(error);
  }

  static isArithmetic(value: Object): boolean {
    return value instanceof ArithmeticException;
  }

  static asFormat(value: Object): FormatException | null {
    return value instanceof FormatException ? value : null;
  }

  static recover(account: string | null, amount: Decimal): string {
    try {
      Ledger.post(account, amount, 0);

      return "posted";
    } catch (e$) {
      if (e$ instanceof KeyNotFoundException) {
        const e = e$;

        return "missing:" + e.message;
      } else if (e$ instanceof ArgumentOutOfRangeException && ((e) => e.paramName === "amount")(e$)) {
        return "amount";
      } else if (e$ instanceof ArgumentException) {
        return "argument";
      } else {
        const e = e$;

        return "other:" + e.message;
      }
    }
  }
}
