/**
 * A value built on first read and kept: `lazyValue(() => table)` returns a
 * reader that runs the factory once. Module-level tables, patterns and
 * configured instances are declared through it, annotated
 * `/* @__PURE__ *\/`, so a module keeps nothing a bundler must retain and a
 * bundle drops what no code reads (spec 011 FR-043). The factory runs on the
 * first read, never at import.
 */
export function lazyValue<T>(factory: () => T): () => T {
  let built = false;
  let value: T;

  return () => {
    if (!built) {
      value = factory();
      built = true;
    }

    return value;
  };
}
