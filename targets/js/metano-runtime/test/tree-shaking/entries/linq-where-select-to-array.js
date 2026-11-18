// `xs.Where(...).Select(...).ToArray()`, as generated code lowers it.
import { linq, select, toArray, where } from "./system/linq/index.js";

export function doubledPositives(xs) {
  return linq(
    xs,
    where((x) => x > 0),
    select((x) => x * 2),
    toArray(),
  );
}
