/**
 * Multicast delegate support for C# event/delegate lowering.
 *
 * A delegate is a plain function tagged with a hidden listener list. When `+=`
 * is used on a function, it's promoted to a multicast delegate via
 * {@link delegateAdd}. The promoted delegate IS still a function with the
 * same signature — callers can't tell the difference.
 *
 * Uses an array (not Set) because C# multicast delegates allow duplicate
 * subscriptions — adding the same handler twice invokes it twice, and
 * `-=` removes only the last matching occurrence.
 *
 * Zero overhead when `+=` is never used: the function stays a plain function.
 */

const DELEGATE_LISTENERS = Symbol("delegate_listeners");

type AnyFunc = (...args: any[]) => any;

type DelegateFunc<T extends AnyFunc> = T & {
  [DELEGATE_LISTENERS]: T[];
};

/** Returns true if the function has been promoted to a multicast delegate. */
export function isDelegate<T extends AnyFunc>(fn: T | null): fn is DelegateFunc<T> {
  return fn !== null && DELEGATE_LISTENERS in fn;
}

/**
 * Creates a multicast delegate from one or more handlers. The returned
 * function calls every handler in insertion order and returns the last
 * handler's return value (matching C# multicast delegate semantics).
 */
export function createDelegate<T extends AnyFunc>(...handlers: T[]): T {
  const listeners = [...handlers];

  const delegate = ((...args: any[]) => {
    let result: any;
    for (const listener of listeners) {
      result = listener(...args);
    }
    return result;
  }) as DelegateFunc<T>;

  Object.defineProperty(delegate, DELEGATE_LISTENERS, {
    value: listeners,
    enumerable: false,
  });

  return delegate as T;
}

/**
 * Adds a handler to a delegate. If the target is a plain function, it's
 * promoted to a multicast delegate first. If the target is null, the handler
 * itself is returned (first subscription on a nullable event).
 */
export function delegateAdd<T extends AnyFunc>(target: T | null, handler: T): T {
  if (target === null) return handler;
  if (isDelegate(target)) {
    target[DELEGATE_LISTENERS].push(handler);
    return target;
  }
  return createDelegate(target, handler);
}

/**
 * Removes a handler from a delegate. Removes the LAST matching occurrence
 * (matching C# `-=` semantics). Returns null when the last handler is
 * removed, or the target unchanged if the handler wasn't found.
 */
export function delegateRemove<T extends AnyFunc>(target: T | null, handler: T): T | null {
  if (target === null) return null;
  if (isDelegate(target)) {
    const list = target[DELEGATE_LISTENERS];
    const idx = list.lastIndexOf(handler);
    if (idx >= 0) list.splice(idx, 1);
    if (list.length === 0) return null;
    if (list.length === 1) return list[0]!;
    return target;
  }
  return target === handler ? null : target;
}
