import { isInt32, isString } from "metano-runtime";

export class BaseGreeter {
  constructor() { }

  greetOther(other: Object): number {
    return -1;
  }

  greetNameTimes(name: string, times: number = 3): number {
    return times;
  }

  greet(other: Object): number;
  greet(name: string, times?: number): number;
  greet(...args: unknown[]): number {
    if (args.length === 1 && typeof args[0] === "object") {
      return this.greetOther(args[0] as Object);
    }

    if (args.length >= 1 && args.length <= 2 && isString(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
      return this.greetNameTimes(args[0] as string, args[1] as number);
    }

    throw new Error("No matching overload for greet");
  }
}
