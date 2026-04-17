// Register factory functions to break circular dependency between
// EnumerableBase (which needs to create subclasses) and the subclasses
// (which extend EnumerableBase).
import { _registerFactories } from "./enumerable-base.ts";
import { WhereEnumerable } from "./where-enumerable.ts";
import { SelectEnumerable } from "./select-enumerable.ts";
import { SelectManyEnumerable } from "./select-many-enumerable.ts";
import { OrderByEnumerable } from "./order-by-enumerable.ts";
import { TakeEnumerable } from "./take-enumerable.ts";
import { SkipEnumerable } from "./skip-enumerable.ts";
import { DistinctEnumerable } from "./distinct-enumerable.ts";
import { GroupByEnumerable } from "./group-by-enumerable.ts";
import { ConcatEnumerable } from "./concat-enumerable.ts";
import { ThenByEnumerable } from "./then-by-enumerable.ts";
import { TakeWhileEnumerable } from "./take-while-enumerable.ts";
import { SkipWhileEnumerable } from "./skip-while-enumerable.ts";
import { DistinctByEnumerable } from "./distinct-by-enumerable.ts";
import { ReverseEnumerable } from "./reverse-enumerable.ts";
import { ZipEnumerable } from "./zip-enumerable.ts";
import { AppendEnumerable } from "./append-enumerable.ts";
import { PrependEnumerable } from "./prepend-enumerable.ts";
import { UnionEnumerable } from "./union-enumerable.ts";
import { IntersectEnumerable } from "./intersect-enumerable.ts";
import { ExceptEnumerable } from "./except-enumerable.ts";

_registerFactories({
  where: (source, predicate) => new WhereEnumerable(source, predicate),
  select: (source, selector) => new SelectEnumerable(source, selector),
  selectMany: (source, selector) => new SelectManyEnumerable(source, selector),
  orderBy: (source, keySelector, descending) =>
    new OrderByEnumerable(source, keySelector, descending),
  take: (source, count) => new TakeEnumerable(source, count),
  skip: (source, count) => new SkipEnumerable(source, count),
  distinct: (source) => new DistinctEnumerable(source),
  groupBy: (source, keySelector) => new GroupByEnumerable(source, keySelector),
  concat: (first, second) => new ConcatEnumerable(first, second),
  thenBy: (source, keySelector, descending) =>
    new ThenByEnumerable(source, keySelector, descending),
  takeWhile: (source, predicate) => new TakeWhileEnumerable(source, predicate),
  skipWhile: (source, predicate) => new SkipWhileEnumerable(source, predicate),
  distinctBy: (source, keySelector) => new DistinctByEnumerable(source, keySelector),
  reverse: (source) => new ReverseEnumerable(source),
  zip: (source, second, resultSelector) => new ZipEnumerable(source, second, resultSelector),
  append: (source, element) => new AppendEnumerable(source, element),
  prepend: (source, element) => new PrependEnumerable(source, element),
  union: (first, second) => new UnionEnumerable(first, second),
  intersect: (first, second) => new IntersectEnumerable(first, second),
  except: (first, second) => new ExceptEnumerable(first, second),
});

// Re-export everything
export { EnumerableBase, type Grouping } from "./enumerable-base.ts";
export { Enumerable } from "./enumerable.ts";
export { SourceEnumerable } from "./source-enumerable.ts";
export { WhereEnumerable } from "./where-enumerable.ts";
export { SelectEnumerable } from "./select-enumerable.ts";
export { SelectManyEnumerable } from "./select-many-enumerable.ts";
export { OrderByEnumerable } from "./order-by-enumerable.ts";
export { TakeEnumerable } from "./take-enumerable.ts";
export { SkipEnumerable } from "./skip-enumerable.ts";
export { DistinctEnumerable } from "./distinct-enumerable.ts";
export { GroupByEnumerable } from "./group-by-enumerable.ts";
export { ConcatEnumerable } from "./concat-enumerable.ts";
export { ThenByEnumerable } from "./then-by-enumerable.ts";
export { TakeWhileEnumerable } from "./take-while-enumerable.ts";
export { SkipWhileEnumerable } from "./skip-while-enumerable.ts";
export { DistinctByEnumerable } from "./distinct-by-enumerable.ts";
export { ReverseEnumerable } from "./reverse-enumerable.ts";
export { ZipEnumerable } from "./zip-enumerable.ts";
export { AppendEnumerable } from "./append-enumerable.ts";
export { PrependEnumerable } from "./prepend-enumerable.ts";
export { UnionEnumerable } from "./union-enumerable.ts";
export { IntersectEnumerable } from "./intersect-enumerable.ts";
export { ExceptEnumerable } from "./except-enumerable.ts";
