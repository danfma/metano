import { UUID, isInt32, isString } from "metano-runtime";
import { Inner } from "./inner";
import { Mood } from "./mood";

export class Day {
  protected static readonly byDayN: unique symbol = Symbol("Day(int n)");

  protected static readonly byDayLabel: unique symbol = Symbol("Day(string label)");

  tag: string;

  label: string;

  mood: Mood = Mood.Calm;

  nested: Inner;

  id: UUID;

  constructor(overload: typeof Day.byDayN, n: number);
  constructor(overload: typeof Day.byDayLabel, label: string);
  constructor();
  constructor(n: number);
  constructor(label: string);
  constructor(...args: unknown[]) {
    if (args.length === 0) {
      this.tag = "\u0000";
      this.label = null as never;
      this.mood = Mood.Calm;
      this.nested = new Inner();
      this.id = undefined as never;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Day.byDayN) {
      const n = args[1] as number;
      this.tag = "a";
      this.label = "";
      this.mood = Mood.Loud;
      this.nested = Inner.fromV(n);
      this.id = UUID.empty;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Day.byDayLabel) {
      const label = args[1] as string;
      const __chain0Arg0 = label.length;

      {
        const n = __chain0Arg0;
        this.tag = "a";
        this.label = "";
        this.mood = Mood.Loud;
        this.nested = Inner.fromV(n);
        this.id = UUID.empty;
      }

      this.label = label;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const n = args[0] as number;
      this.tag = "a";
      this.label = "";
      this.mood = Mood.Loud;
      this.nested = Inner.fromV(n);
      this.id = UUID.empty;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const label = args[0] as string;
      const __chain0Arg0 = label.length;

      {
        const n = __chain0Arg0;
        this.tag = "a";
        this.label = "";
        this.mood = Mood.Loud;
        this.nested = Inner.fromV(n);
        this.id = UUID.empty;
      }

      this.label = label;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromN(n: number): Day {
    return new Day(Day.byDayN, n);
  }

  static fromLabel(label: string): Day {
    return new Day(Day.byDayLabel, label);
  }
}
