/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { isInt32, isString } from "metano-runtime";

export class Estimate {
  protected static readonly byEstimatePoints: unique symbol = Symbol("Estimate(int points)");

  protected static readonly byEstimatePointsNote: unique symbol = Symbol(
    "Estimate(int points, string note)",
  );

  protected static readonly byEstimateFirstSecond: unique symbol = Symbol(
    "Estimate(Estimate first, Estimate second)",
  );

  static readonly none: Estimate = Estimate.fromPoints(0);

  readonly points: number = 0;

  readonly note: string;

  constructor(overload: typeof Estimate.byEstimatePoints, points: number);
  constructor(overload: typeof Estimate.byEstimatePointsNote, points: number, note: string);
  constructor(overload: typeof Estimate.byEstimateFirstSecond, first: Estimate, second: Estimate);
  constructor(points: number, note: string);
  constructor(first: Estimate, second: Estimate);
  constructor(points: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Estimate.byEstimatePoints) {
      const points = args[1] as number;
      const __chain0Arg0 = points;
      const __chain0Arg1 = "";

      {
        const points = __chain0Arg0;
        const note = __chain0Arg1;
        this.points = points;
        this.note = note;
      }

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Estimate.byEstimatePointsNote) {
      const points = args[1] as number;
      const note = args[2] as string;
      this.points = points;
      this.note = note;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Estimate.byEstimateFirstSecond) {
      const first = args[1] as Estimate;
      const second = args[2] as Estimate;
      const __chain0Arg0 = first.points + second.points;
      const __chain0Arg1 = "combined";

      {
        const points = __chain0Arg0;
        const note = __chain0Arg1;
        this.points = points;
        this.note = note;
      }

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isString(args[1])) {
      const points = args[0] as number;
      const note = args[1] as string;
      this.points = points;
      this.note = note;

      return;
    }

    if (args.length === 2 && args[0] instanceof Estimate && args[1] instanceof Estimate) {
      const first = args[0] as Estimate;
      const second = args[1] as Estimate;
      const __chain0Arg0 = first.points + second.points;
      const __chain0Arg1 = "combined";

      {
        const points = __chain0Arg0;
        const note = __chain0Arg1;
        this.points = points;
        this.note = note;
      }

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const points = args[0] as number;
      const __chain0Arg0 = points;
      const __chain0Arg1 = "";

      {
        const points = __chain0Arg0;
        const note = __chain0Arg1;
        this.points = points;
        this.note = note;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromPoints(points: number): Estimate {
    return new Estimate(Estimate.byEstimatePoints, points);
  }

  static fromPointsNote(points: number, note: string): Estimate {
    return new Estimate(Estimate.byEstimatePointsNote, points, note);
  }

  static fromFirstSecond(first: Estimate, second: Estimate): Estimate {
    return new Estimate(Estimate.byEstimateFirstSecond, first, second);
  }

  addPoints(points: number): Estimate {
    return Estimate.fromPointsNote(this.points + points, this.note);
  }

  addOther(other: Estimate): Estimate {
    return Estimate.fromFirstSecond(this, other);
  }

  add(points: number): Estimate;
  add(other: Estimate): Estimate;
  add(...args: unknown[]): Estimate {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addPoints(args[0] as number);
    }

    if (args.length === 1 && args[0] instanceof Estimate) {
      return this.addOther(args[0] as Estimate);
    }

    throw new Error("No matching overload for add");
  }

  toString(): string {
    return this.note.length === 0 ? `${this.points} pt` : `${this.points} pt (${this.note})`;
  }
}
