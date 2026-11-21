import type { RuntimeType } from "metano-runtime";

export interface IChecker {
  check<T>(value: Object, T$type: RuntimeType<T>): boolean;
}

// ---
import { isString, type RuntimeType } from "metano-runtime";
import type { IChecker } from "./i-checker";

export class BaseChecker implements IChecker {
  constructor() { }

  checkString(value: string): string {
    return value;
  }

  checkObject<T>(value: Object, T$type: RuntimeType<T>): boolean {
    return false;
  }

  check(value: string): string;
  check<T>(value: Object, T$type: RuntimeType<T>): boolean;
  check(...args: unknown[]): any {
    if (args.length === 1 && isString(args[0])) {
      return this.checkString(args[0] as string);
    }

    if (args.length === 2) {
      return this.checkObject(args[0] as any, args[1] as any);
    }

    throw new Error("No matching overload for check");
  }
}

// ---
import { isType, type RuntimeType } from "metano-runtime";
import { BaseChecker } from "./base-checker";

export class ReifiedChecker extends BaseChecker {
  constructor() {
    super();
  }

  override checkObject<T>(value: Object, T$type: RuntimeType<T>): boolean {
    return isType(value, T$type);
  }
}
