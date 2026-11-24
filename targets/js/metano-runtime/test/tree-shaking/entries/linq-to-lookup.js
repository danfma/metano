// `xs.ToLookup(k)`, as generated code lowers it (#664).
import { linq, toLookup } from "./system/linq/index.js";

export function lookup(xs) {
  return linq(xs, toLookup((x) => x.key));
}
