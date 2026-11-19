import { boxedText } from "./boxed-text.ts";
import type { RuntimeType } from "./runtime-type.ts";

/**
 * A C# boxing conversion of an enum or `[InlineWrapper]` value (spec 011 FR-050): the raw value
 * plus the descriptor that lets `GetType()`, `ToString()` and `Equals` answer as .NET does for
 * an `object`. Generic instantiations, plain numbers, strings and booleans are never boxed.
 */
export class Boxed<T> {
  readonly $boxed = true;

  constructor(
    readonly value: T,
    readonly type: RuntimeType<T>,
  ) {}

  toString(): string {
    return boxedText(this.value, this.type);
  }

  valueOf(): T {
    return this.value;
  }

  toJSON(): T {
    return this.value;
  }
}

/** Boxes `value` as an instance of `type`: `object o = Color.Red` is `box(Color.Red, Color$runtimeType)`. */
export function box<T>(value: T, type: RuntimeType<T>): Boxed<T> {
  return new Boxed(value, type);
}

/** Boxes a nullable operand, evaluating it once: `null` stays `null`. */
export function boxNullable<T>(value: T | null, type: RuntimeType<T>): Boxed<T> | null {
  return value === null ? null : box(value, type);
}
