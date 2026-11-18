// `throw new InvalidOperationException()`, as generated code imports it.
import { InvalidOperationException } from "./system/exceptions/invalid-operation-exception.js";

export function fail() {
  throw new InvalidOperationException();
}
