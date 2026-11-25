// `xs.Contains(x)` over a lazy sequence, as generated code lowers it (#664).
import { contains, linq } from "./system/linq/index.js";

export function has(xs, x) {
  return linq(xs, contains(x));
}
