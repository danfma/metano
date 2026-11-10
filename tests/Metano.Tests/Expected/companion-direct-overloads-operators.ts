import { isInt64 } from "metano-runtime";
import type { UId } from "./u-id";

export type Ticks = bigint & { readonly __brand: "Ticks" };

export namespace Ticks {
  export function create(value: bigint): Ticks {
    return value as Ticks;
  }

  export function toString(value: Ticks): string {
    return String(value);
  }

  function op_AdditionTicksInt64(a: Ticks, b: bigint): Ticks {
    return Ticks.create(BigInt.asIntN(64, a + b));
  }

  function op_AdditionTicksUId(a: Ticks, b: UId): Ticks {
    return Ticks.create(BigInt.asIntN(64, a + b));
  }

  export function op_Addition(a: Ticks, b: bigint): Ticks;
  export function op_Addition(a: Ticks, b: UId): Ticks;
  export function op_Addition(...args: unknown[]): Ticks {
    if (args.length === 2 && typeof args[0] === "bigint" && isInt64(args[1])) {
      return op_AdditionTicksInt64(args[0] as Ticks, args[1] as bigint);
    }

    if (args.length === 2 && typeof args[0] === "bigint" && typeof args[1] === "bigint") {
      return op_AdditionTicksUId(args[0] as Ticks, args[1] as UId);
    }

    throw new Error("No matching overload for op_Addition");
  }
}
