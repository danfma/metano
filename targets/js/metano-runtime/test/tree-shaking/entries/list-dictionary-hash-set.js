// `List`, `Dictionary` and `HashSet` together (spec 011 NFR-008): the exception classes and
// `elementEquals` they share count once.
import { Dictionary } from "./system/collections/dictionary.js";
import { HashSet } from "./system/collections/hash-set.js";
import { List } from "./system/collections/list.js";

export const create = () => [List.of([1]), Dictionary.of([["a", 1]]).keys, HashSet.of([1])];
