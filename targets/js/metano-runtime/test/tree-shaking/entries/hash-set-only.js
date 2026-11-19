// `HashSet` alone (spec 011 NFR-008).
import { HashSet } from "./system/collections/hash-set.js";

export const create = () => HashSet.of([1, 2]);
