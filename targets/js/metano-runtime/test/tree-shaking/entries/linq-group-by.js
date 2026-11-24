// `xs.GroupBy(k)`, as generated code lowers it (#664).
import { groupBy, linq } from "./system/linq/index.js";

export function groups(xs) {
  return linq(xs, groupBy((x) => x.key));
}
