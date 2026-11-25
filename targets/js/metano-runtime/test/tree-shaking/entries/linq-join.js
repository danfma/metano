// `xs.Join(ys, ...)`, as generated code lowers it (#664).
import { join, linq } from "./system/linq/index.js";

export function joined(xs, ys) {
  return linq(
    xs,
    join(
      ys,
      (o) => o.key,
      (i) => i.key,
      (o, i) => [o, i],
    ),
  );
}
