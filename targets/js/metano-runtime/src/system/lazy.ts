import { InvalidOperationException } from "./exceptions/index.ts";

type LazyState<T> =
  | { readonly kind: "pending"; readonly factory: () => T }
  | { readonly kind: "running" }
  | { readonly kind: "created"; readonly value: T }
  | { readonly kind: "failed"; readonly error: unknown };

/**
 * `System.Lazy<T>` with a factory: the factory runs on the first read of `value`, once. A
 * factory that throws throws the same exception on every later read (the default
 * `ExecutionAndPublication` mode caches it), and one that reads `value` itself throws
 * `InvalidOperationException`.
 */
export class Lazy<T> {
  private state: LazyState<T>;

  constructor(factory: () => T) {
    this.state = { kind: "pending", factory };
  }

  /** `Lazy<T>.IsValueCreated`: `true` once the factory returned. */
  get isValueCreated(): boolean {
    return this.state.kind === "created";
  }

  /** `Lazy<T>.Value`. */
  get value(): T {
    const state = this.state;

    if (state.kind === "created") {
      return state.value;
    }

    if (state.kind === "failed") {
      throw state.error;
    }

    if (state.kind === "running") {
      throw new InvalidOperationException(
        "ValueFactory attempted to access the Value property of this instance.",
      );
    }

    return this.create(state.factory);
  }

  private create(factory: () => T): T {
    this.state = { kind: "running" };

    try {
      const value = factory();

      this.state = { kind: "created", value };

      return value;
    } catch (error) {
      this.state = { kind: "failed", error };
      throw error;
    }
  }
}
