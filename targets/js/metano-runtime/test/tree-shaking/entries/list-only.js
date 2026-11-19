// `List` alone, its full surface reachable (spec 011 NFR-008).
import { List } from "./system/collections/list.js";

export const create = () => List.of([1, 2]);
