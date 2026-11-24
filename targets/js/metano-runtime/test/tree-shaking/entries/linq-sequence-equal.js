// `xs.SequenceEqual(ys)`, as generated code lowers it (#664).
import { linq, sequenceEqual } from "./system/linq/index.js";

export function same(xs, ys) {
  return linq(xs, sequenceEqual(ys));
}
