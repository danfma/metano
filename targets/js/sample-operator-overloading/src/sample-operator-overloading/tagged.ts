/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, isString } from "metano-runtime";

export class Tagged<T> {
  readonly kind: string;

  constructor(value: T, label: string);
  constructor(code: number);
  constructor(value: T);
  constructor(...args: unknown[]) {
    if (args.length === 2 && isString(args[1])) {
      const value = args[0] as T;
      const label = args[1] as string;
      this.kind = `value:${label}`;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const code = args[0] as number;
      this.kind = "code";

      return;
    }

    if (args.length === 1) {
      const value = args[0] as T;
      this.kind = "value";

      return;
    }

    throw new Error("No matching constructor");
  }
}
