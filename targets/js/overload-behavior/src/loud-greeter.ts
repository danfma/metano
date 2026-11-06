import { isInt32, isString } from "metano-runtime";
import { BaseGreeter } from "./base-greeter";

export class LoudGreeter extends BaseGreeter {
  constructor() {
    super();
  }

  greetNameTimes$(name: string, times: number = 9): number {
    return times * 10;
  }

  override greet(other: Object): number;
  override greet(name: string, times?: number): number;
  override greet(...args: unknown[]): number {
    if (args.length >= 1 && args.length <= 2 && isString(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
      return this.greetNameTimes$(args[0] as string, args[1] as number);
    }

    return super.greet(...args as [never]);
  }
}
