/**
 * Non-destructive record update — the TypeScript side of C#'s
 * `source with { X = value }`.
 *
 * C# compiles `with` to the record's copy constructor (a member-wise clone that
 * does not run the primary constructor) followed by the `init` assignments.
 * Mirroring that, the copy keeps the source's prototype and every own
 * enumerable field — including members the primary constructor does not take —
 * and then applies every own key of `overrides`: the generated call passes
 * exactly the members the `with` assigns. A key that is absent keeps the copied
 * value. A key that is present is an assignment even when its value is
 * `undefined` — an absent optional field of a `[PlainObject]` value, read as
 * C# `null` — so it sets the member to `null`, never back to the copied value.
 * A generated `with` only assigns `null` where C# can, so a non-nullable member
 * never receives one; a hand-written caller passing `undefined` for such a
 * member gets `null` too.
 *
 * An override is assigned on the copy, so a member declared with an accessor
 * pair runs its setter. An override naming a member the copy cannot be
 * assigned — a getter without a setter, or a non-writable property — is
 * skipped: C# rejects `with` on computed members, and a property whose custom
 * `init` body is not transpiled has no setter (the compiler reports MS0001 for
 * that `with`). JavaScript `#private` fields are not copied; generated records
 * do not declare them.
 */
export function copyRecordWith<T extends object>(source: T, overrides?: Partial<T>): T {
  const copy: T = Object.create(Object.getPrototypeOf(source));

  Object.assign(copy, source);

  if (overrides !== undefined) {
    assignOverrides(copy, overrides);
  }

  return copy;
}

function assignOverrides<T extends object>(target: T, overrides: Partial<T>): void {
  for (const key of Object.keys(overrides) as (keyof T & string)[]) {
    if (isAssignable(target, key)) {
      const value = overrides[key] ?? null;

      target[key] = value as T[keyof T & string];
    }
  }
}

/** Whether assigning `key` on `target` would succeed rather than throw. */
function isAssignable(target: object, key: string): boolean {
  const descriptor = findPropertyDescriptor(target, key);

  if (descriptor === undefined) {
    return true;
  }

  if (descriptor.get !== undefined || descriptor.set !== undefined) {
    return descriptor.set !== undefined;
  }

  return descriptor.writable === true;
}

function findPropertyDescriptor(target: object, key: string): PropertyDescriptor | undefined {
  for (
    let current: object | null = target;
    current !== null;
    current = Object.getPrototypeOf(current)
  ) {
    const descriptor = Object.getOwnPropertyDescriptor(current, key);

    if (descriptor !== undefined) {
      return descriptor;
    }
  }

  return undefined;
}
