/**
 * The factory of an abstract or interface base spec: a polymorphic read never
 * calls it, so reaching it fails the test.
 */
export function abstractFactory(): never {
  throw new Error("An abstract base factory was called.");
}
