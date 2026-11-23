import { isDecimal } from "../../type-checking/index.ts";
import { Decimal } from "../decimal.ts";
import type { PrimitiveType } from "./runtime-type.ts";

const decimalZero = (): Decimal => new Decimal(0);

/**
 * `DecimalType` with `default` and `create` factories, kept in its own module
 * so `castTo`, `isType`, and `getRuntimeType` do not import `decimal.js`.
 */
export const DecimalDefaultType: PrimitiveType<Decimal> = {
  kind: "primitive",
  name: "Decimal",
  fullName: "System.Decimal",
  namespace: "System",
  primitive: "Decimal",
  test: isDecimal,
  default: decimalZero,
  create: decimalZero,
};
