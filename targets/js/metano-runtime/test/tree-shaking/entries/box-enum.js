// A boxed enum: the descriptor literal generated code emits, `box(...)` at the boxing
// conversion and the `ToString()` of the box.
import { box } from "./system/reflection/box.js";

const Color$runtimeType = {
  kind: "enum",
  name: "Color",
  fullName: "App.Color",
  namespace: "App",
  values: [0, 1],
  names: ["Red", "Green"],
  isFlags: false,
  test: (v) => v === 0 || v === 1,
};

export function describe() {
  return String(box(1, Color$runtimeType));
}
