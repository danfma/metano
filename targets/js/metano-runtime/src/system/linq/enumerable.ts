import { EnumerableBase } from "./enumerable-base.ts";
import { SourceEnumerable } from "./source-enumerable.ts";

/**
 * Static factory for creating LINQ enumerable sequences.
 * Modeled after System.Linq.Enumerable in .NET.
 */
export class Enumerable {
  private constructor() {}

  /** Wraps an iterable (array, Set, Map, generator, etc.) as an Enumerable. */
  static from<T>(source: Iterable<T>): EnumerableBase<T> {
    if (source instanceof EnumerableBase) return source;
    return new SourceEnumerable(source);
  }

  /** Creates an empty sequence. */
  static empty<T>(): EnumerableBase<T> {
    return new SourceEnumerable<T>([]);
  }

  /** Generates a sequence of integers within a specified range. */
  static range(start: number, count: number): EnumerableBase<number> {
    return new SourceEnumerable(
      (function* () {
        for (let i = 0; i < count; i++) yield start + i;
      })(),
    );
  }

  /** Generates a sequence that contains one repeated value. */
  static repeat<T>(element: T, count: number): EnumerableBase<T> {
    return new SourceEnumerable(
      (function* () {
        for (let i = 0; i < count; i++) yield element;
      })(),
    );
  }
}
