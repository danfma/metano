// `Dictionary` alone with its key and value views (spec 011 NFR-008).
import { Dictionary } from "./system/collections/dictionary.js";

export const create = () => Dictionary.of([["a", 1]]).keys;
