import { isInt32, isString } from "metano-runtime";

export type Meters = number & { readonly __brand: "Meters" };

export namespace Meters {
  export function create(value: number): Meters {
    return value as Meters;
  }

  export function toString(value: Meters): string {
    return String(value);
  }

  function of_A(a: number = 5): Meters {
    return Meters.create(a);
  }

  function of_SRest(s: string, ...rest: number[]): Meters {
    return Meters.create(rest.length);
  }

  export function of_(a?: number): Meters;
  export function of_(s: string, ...rest: number[]): Meters;
  export function of_(...args: unknown[]): Meters {
    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      return of_A(args[0] as number);
    }

    if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => isInt32(value))) {
      return of_SRest(args[0] as string, ...args.slice(1) as number[]);
    }

    throw new Error("No matching overload for of");
  }
}
