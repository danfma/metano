/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { isInt32, isString } from "metano-runtime";
import { Estimate } from "./estimate";

export function total(estimates: Estimate[]): Estimate {
  let total = Estimate.none;
  for (const estimate of estimates) {
    total = total.addOther(estimate);
  }

  return total;
}

export function plan(estimate: Estimate, sprints: number): Estimate {
  return scaleNote(scaleFactor(estimate, sprints), "planned");
}

export function scaleFactor(estimate: Estimate, factor: number): Estimate {
  return Estimate.fromPointsNote(estimate.points * factor, estimate.note);
}

export function scaleNote(estimate: Estimate, note: string): Estimate {
  return Estimate.fromPointsNote(estimate.points, note);
}

export function scale(estimate: Estimate, factor: number): Estimate;
export function scale(estimate: Estimate, note: string): Estimate;
export function scale(...args: unknown[]): Estimate {
  if (args.length === 2 && args[0] instanceof Estimate && isInt32(args[1])) {
    return scaleFactor(args[0] as Estimate, args[1] as number);
  }

  if (args.length === 2 && args[0] instanceof Estimate && isString(args[1])) {
    return scaleNote(args[0] as Estimate, args[1] as string);
  }

  throw new Error("No matching overload for scale");
}
