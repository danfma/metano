import { throwModified } from "./throw-helpers.ts";

/**
 * The enumerator of every immutable builder: the inner collection's items,
 * checked against the builder's own version on every step (.NET's builder
 * enumerators throw on any mutation, a removal included). Internal.
 */
export class BuilderIterator<T> implements Iterator<T> {
  private readonly builder: { readonly _version: number };
  private readonly version: number;
  private readonly inner: Iterator<T>;

  constructor(builder: { readonly _version: number }, inner: Iterator<T>) {
    this.builder = builder;
    this.version =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? builder._version : 0;
    this.inner = inner;
  }

  next(): IteratorResult<T> {
    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this.builder._version !== this.version
    ) {
      throwModified();
    }

    return this.inner.next();
  }
}
