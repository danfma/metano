/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, isString } from "metano-runtime";

export class Tagged<T> {
  protected static readonly byTaggedValue: unique symbol = Symbol("Tagged(T value)");

  protected static readonly byTaggedCode: unique symbol = Symbol("Tagged(int code)");

  protected static readonly byTaggedValueLabel: unique symbol = Symbol(
    "Tagged(T value, string label)",
  );

  readonly kind: string;

  constructor(overload: typeof Tagged.byTaggedValue, value: T);
  constructor(overload: typeof Tagged.byTaggedCode, code: number);
  constructor(overload: typeof Tagged.byTaggedValueLabel, value: T, label: string);
  constructor(value: T, label: string);
  constructor(code: number);
  constructor(value: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Tagged.byTaggedValue) {
      const value = args[1] as T;
      this.kind = "value";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Tagged.byTaggedCode) {
      const code = args[1] as number;
      this.kind = "code";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Tagged.byTaggedValueLabel) {
      const value = args[1] as T;
      const label = args[2] as string;
      this.kind = `value:${label}`;

      return;
    }

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

  static fromValue<T>(value: T): Tagged<T> {
    return new Tagged(Tagged.byTaggedValue, value);
  }

  static fromCode<T>(code: number): Tagged<T> {
    return new Tagged(Tagged.byTaggedCode, code);
  }

  static fromValueLabel<T>(value: T, label: string): Tagged<T> {
    return new Tagged(Tagged.byTaggedValueLabel, value, label);
  }
}
